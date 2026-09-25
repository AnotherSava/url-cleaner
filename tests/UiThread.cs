using System.Collections.Concurrent;

namespace UrlCleaner.Tests;

/// <summary>
/// Runs a test body on one thread that pumps its own continuations, as the WinForms UI thread does for the app. Session
/// state is meant for that one thread, and overlapping runs only behave as they do in the app when their continuations
/// take turns on it.
/// </summary>
public sealed class UiThread : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    /// <summary>
    /// Runs <paramref name="body"/> and pumps its continuations on the calling thread until it completes.
    /// </summary>
    public static void Run(Func<Task> body)
    {
        var previous = Current;
        var thread = new UiThread();
        SetSynchronizationContext(thread);
        try
        {
            var task = body();
            task.ContinueWith(_ => thread._queue.CompleteAdding(), TaskScheduler.Default);
            foreach (var (callback, state) in thread._queue.GetConsumingEnumerable())
                callback(state);

            task.GetAwaiter().GetResult();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}
