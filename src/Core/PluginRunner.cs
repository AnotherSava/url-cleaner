using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace UrlCleaner;

/// <summary>
/// How a plugin call ended: with a validated answer, or with a one-line reason it failed.
/// </summary>
public abstract record PluginOutcome;

public sealed record Answered(PluginAnswer Answer) : PluginOutcome;

public sealed record Failed(string Reason) : PluginOutcome;

/// <summary>
/// Runs one plugin call: starts the process, writes the request to stdin, and reads one answer from stdout, within the
/// manifest's deadline plus a short grace for the pipes to close. Each call is its own process.
/// </summary>
public static class PluginRunner
{
    // After the process exits, its pipes should close at once. A process it started can still hold them open.
    private static readonly TimeSpan OutputGrace = TimeSpan.FromSeconds(2);

    // One JSON answer is small. A plugin writing past this is killed rather than buffered, so it can't exhaust memory.
    private const int StdoutLimit = 1024 * 1024;

    private const int StderrTailLength = 2000;

    // Stdin must not start with a byte-order mark: Node's JSON.parse rejects it.
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static Task<PluginOutcome> CopyAsync(PluginManifest plugin, string text) =>
        CallAsync(plugin, "copy", PluginProtocol.CopyRequest(text), isActionCall: false);

    public static Task<PluginOutcome> ActionAsync(PluginManifest plugin, string text, string action, IReadOnlyDictionary<string, string> fields, JsonElement? state) =>
        CallAsync(plugin, "action", PluginProtocol.ActionRequest(text, action, fields, state), isActionCall: true);

    private static async Task<PluginOutcome> CallAsync(PluginManifest plugin, string callType, string request, bool isActionCall)
    {
        using var process = new Process();
        string? executable = null;
        try
        {
            executable = plugin.FindExecutable(Environment.GetEnvironmentVariable("PATH"));
            if (executable == null)
                return Fail(plugin, callType, $"can't find \"{plugin.Run[0]}\" in the plugin folder or on PATH");
            if (PluginManifest.IsBatchFile(executable))
                return Fail(plugin, callType, $"won't start the batch file {executable}");

            process.StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = plugin.Folder,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Utf8,
                StandardOutputEncoding = Utf8,
                StandardErrorEncoding = Utf8
            };
            foreach (var argument in plugin.Run.Skip(1))
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
        }
        catch (Exception e) when (e is Win32Exception or ArgumentException or InvalidOperationException)
        {
            return Fail(plugin, callType, $"couldn't start {executable ?? plugin.Run[0]}: {e.Message}");
        }

        PluginJob.Assign(process);
        using var callJob = CallJob.Create(process);

        // Ends the process and everything it started: the whole call job where there is one, and the live process tree.
        void EndAll()
        {
            callJob?.Terminate();
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is AggregateException or Win32Exception or InvalidOperationException or NotSupportedException)
            {
                Logger.Warn($"Plugin {plugin.Id}: couldn't end every process of the call: {e.Message}");
            }
        }

        // Both readers start before the request is written, so a plugin that fills a pipe before reading can't deadlock.
        var stdoutOverflowed = false;
        var stdout = ReadCappedAsync(process.StandardOutput, StdoutLimit, () =>
        {
            stdoutOverflowed = true;
            EndAll();
        });
        var stderr = ReadTailAsync(process.StandardError, StderrTailLength);
        var write = WriteRequestAsync(process, request);

        var timedOut = false;
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(plugin.TimeoutSeconds)))
        {
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                // Cancelling the wait doesn't stop the process, and a write the plugin never reads stays blocked until this.
                timedOut = true;
                EndAll();
                await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(OutputGrace));
            }
        }

        // A process the plugin started can keep the pipes open after the plugin exits: stdout and stderr, or stdin with a
        // request too big for the pipe's buffer. Ending it releases them, and the readers blocked on them. Which pipes had
        // closed is read before that, since ending the process closes the rest.
        var pipes = Task.WhenAll(stdout, stderr, (Task)write);
        var pipesClosed = await Task.WhenAny(pipes, Task.Delay(OutputGrace)) == pipes;
        var stdoutEnded = stdout.IsCompletedSuccessfully;
        var leftover = pipesClosed ? null : $"{Environment.NewLine}  A process it started still held its pipes {OutputGrace.TotalSeconds:0} seconds after it exited; ended it.";
        if (!pipesClosed)
            EndAll();

        var writeDetail = write.IsCompleted ? write.Result : "the request was still being written";
        var detail = Detail(stderr.IsCompletedSuccessfully ? Tail(stderr.Result) : "", writeDetail) + leftover;

        // The first reason that applies: the deadline, the output limit, the exit code, then the output.
        string? reason = null;
        PluginAnswer? answer = null;
        if (timedOut)
            reason = $"didn't answer within {plugin.TimeoutSeconds} second{(plugin.TimeoutSeconds == 1 ? "" : "s")}";
        else if (stdoutOverflowed)
            reason = $"wrote more than {StdoutLimit / (1024 * 1024)} MB to stdout";
        else if (process.ExitCode != 0)
            reason = $"exited with code {process.ExitCode}";
        else if (!stdoutEnded)
            reason = $"its output didn't end within {OutputGrace.TotalSeconds:0} seconds of exiting, probably held open by a process it started";
        else
        {
            try
            {
                answer = PluginProtocol.ParseAnswer(stdout.Result, isActionCall);
            }
            catch (ProtocolException e)
            {
                reason = $"gave an invalid answer: {e.Message}";
            }
        }

        if (reason != null)
            return Fail(plugin, callType, reason, detail);

        if (detail.Length > 0)
            Logger.Info($"Plugin {plugin.Id} {callType} call answered {answer!.GetType().Name}.{detail}");
        return new Answered(answer!);
    }

    /// <summary>
    /// Reads to the end, or until more than <paramref name="limit"/> characters arrive, when it calls
    /// <paramref name="onOverflow"/> and stops.
    /// </summary>
    private static async Task<string> ReadCappedAsync(StreamReader reader, int limit, Action onOverflow)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (text.Length + read > limit)
            {
                onOverflow();
                break;
            }

            text.Append(buffer, 0, read);
        }

        return text.ToString();
    }

    /// <summary>
    /// Reads to the end, keeping only about the last <paramref name="keep"/> characters.
    /// </summary>
    private static async Task<string> ReadTailAsync(StreamReader reader, int keep)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > 2 * keep)
                text.Remove(0, text.Length - keep);
        }

        return text.ToString();
    }

    /// <summary>
    /// Writes the request and closes stdin. Returns why the write failed, or <c>null</c>. A plugin that exits without
    /// reading makes it fail; that is detail for the log, never the reason a call failed.
    /// </summary>
    private static async Task<string?> WriteRequestAsync(Process process, string request)
    {
        try
        {
            await process.StandardInput.WriteAsync(request).ConfigureAwait(false);
            process.StandardInput.Close();
            return null;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            return e.Message;
        }
    }

    private static Failed Fail(PluginManifest plugin, string callType, string reason, string detail = "")
    {
        Logger.Error($"Plugin {plugin.Id} {callType} call failed: {reason}.{detail}");
        return new Failed(reason);
    }

    private static string Detail(string errorOutput, string? writeFailure)
    {
        var detail = new StringBuilder();
        if (writeFailure != null)
            detail.Append($"{Environment.NewLine}  Writing the request failed: {writeFailure}");
        if (errorOutput.Length > 0)
            detail.Append($"{Environment.NewLine}  stderr: {errorOutput}");
        return detail.ToString();
    }

    /// <summary>
    /// The last <see cref="StderrTailLength"/> characters, never starting on the second half of a surrogate pair.
    /// </summary>
    private static string Tail(string text)
    {
        if (text.Length <= StderrTailLength)
            return text.TrimEnd();

        var start = text.Length - StderrTailLength;
        if (char.IsLowSurrogate(text[start]))
            start++;
        return "…" + text[start..].TrimEnd();
    }
}

/// <summary>
/// On Windows, every plugin process joins one job object that kills its members when the job's last handle closes. The
/// host holds that handle for its whole life, so plugins and the children they start end with the host however it ends,
/// including a forced stop that never runs its exit code.
/// </summary>
internal static class PluginJob
{
    private static readonly Lazy<IntPtr> Job = new(Create);

    public static void Assign(Process process)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var job = Job.Value;
        if (job == IntPtr.Zero || !JobApi.AssignProcessToJobObject(job, process.Handle))
            Logger.Warn($"Plugin process {process.Id} couldn't join the job object; it won't be stopped with the app (error {Marshal.GetLastWin32Error()})");
    }

    private static IntPtr Create()
    {
        var job = JobApi.CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
            return IntPtr.Zero;

        var info = new JobApi.ExtendedLimitInfo { BasicLimitInformation = { LimitFlags = JobApi.LimitKillOnJobClose } };
        return JobApi.SetInformationJobObject(job, JobApi.ExtendedLimitInformationClass, ref info, (uint)Marshal.SizeOf<JobApi.ExtendedLimitInfo>()) ? job : IntPtr.Zero;
    }
}

/// <summary>
/// On Windows, a job holding one call's process and everything it starts, nested inside the host's job. Terminating it
/// ends processes that a walk of the live process tree misses, such as a child whose parent has already exited.
/// </summary>
internal sealed class CallJob : IDisposable
{
    private readonly IntPtr _handle;

    private CallJob(IntPtr handle) => _handle = handle;

    public static CallJob? Create(Process process)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var handle = JobApi.CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
            return null;

        if (JobApi.AssignProcessToJobObject(handle, process.Handle))
            return new CallJob(handle);

        Logger.Warn($"Plugin process {process.Id} couldn't join its call's job object (error {Marshal.GetLastWin32Error()})");
        JobApi.CloseHandle(handle);
        return null;
    }

    public void Terminate() => JobApi.TerminateJobObject(_handle, 1);

    public void Dispose() => JobApi.CloseHandle(_handle);
}

internal static class JobApi
{
    public const int ExtendedLimitInformationClass = 9;
    public const uint LimitKillOnJobClose = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimitInfo info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    public struct BasicLimitInfo
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExtendedLimitInfo
    {
        public BasicLimitInfo BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
