<#
  The `tray-menu` figure: the tray icon's menu, open.

  The menu is a WinForms ContextMenuStrip that NotifyIcon opens on a right-click,
  so this posts NotifyIcon's hidden window the callback message the shell posts
  for one: WM_USER + 1024 with a bare WM_RBUTTONUP in lParam (NotifyIcon never
  sends NIM_SETVERSION, and reads lParam alone). That runs NotifyIcon's own
  ShowContextMenu, so the frame holds the menu a user gets, with the plugin items
  the Opening handler adds. Re-check both facts in NotifyIcon.cs in dotnet/winforms
  after a major .NET upgrade.

  NotifyIcon's window has nothing that tells it apart from the clipboard monitor's:
  both are hidden, untitled, and of the class WinForms registers for class style 0.
  So the message goes to every such window, and the monitor's passes it to
  DefWindowProc, which ignores it. The dropdown's own hidden window, once it
  exists, has a class of its own (drop-shadow style), so it is not among them.

  ShowContextMenu opens the menu at the pointer, so the pointer goes near the tray
  corner of the primary display first and back afterwards. ShowContextMenu also
  calls SetForegroundWindow on NotifyIcon's window, which can take focus from
  whatever had it. The menu is closed in `finally` by a posted Escape, which
  reaches the dropdown through the app's message loop, then by WM_CANCELMODE if
  that did not do it. Both are posted rather than typed, so a key the user presses
  meanwhile does not go to the menu.

  -Method Alpha with -Popup: photographed over a black and a white backdrop that
  never take focus, since the dropdown closes when the app loses activation.

  No frame step. Unlike a native menu, DWM does not round a ToolStripDropDown or
  draw it a border: the capture comes back square and fully opaque, with the 1px
  grey border the ToolStrip renderer draws on all four sides, which reads on a
  light page and a dark one. winframe.py would add a rounded ring the real menu
  does not have.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $env:USERPROFILE '.claude\skills\docs-relevance\scripts\windows-capture.ps1')

$WM_TRAYMOUSEMESSAGE = 0x0800
$WM_RBUTTONUP = 0x0205
$WM_CANCELMODE = 0x001F
$WM_KEYDOWN = 0x0100
$WM_KEYUP = 0x0101
$VK_ESCAPE = 0x1B
$IconClass = 'WindowsForms10.Window.0.app.*'
$AnyWinForms = 'WindowsForms10.Window.*'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out = Join-Path $repo 'docs\screenshots\tray-menu.png'

$app = @(Get-Process -Name 'UrlCleaner' -ErrorAction SilentlyContinue)
if ($app.Count -ne 1) { throw "Expected one UrlCleaner process, found $($app.Count). Deploy the working tree and re-run." }
$appPid = $app[0].Id

# The menu is built in TrayApplicationContext.cs, so a build older than that file
# would photograph a menu the code no longer makes.
$dll = Join-Path (Split-Path -Parent $app[0].Path) 'UrlCleaner.dll'
$menuSource = Join-Path $repo 'src\TrayApplicationContext.cs'
if ((Get-Item $dll).LastWriteTime -lt (Get-Item $menuSource).LastWriteTime) { throw "The running UrlCleaner ($dll) is older than src/TrayApplicationContext.cs. Deploy the working tree and re-run." }

function Get-OpenWindows { return @(Find-ProcessWindows -ProcessId $appPid -Class $AnyWinForms -VisibleOnly) }

# UrlCleaner's visible windows, once none is left or two seconds have passed.
function Wait-MenuClosed {
    for ($i = 0; $i -lt 20; $i++) {
        $left = Get-OpenWindows
        if ($left.Count -eq 0) { return @() }
        Start-Sleep -Milliseconds 100
    }
    return $left
}

if ((Get-OpenWindows).Count -gt 0) { throw 'UrlCleaner already has a window open, its menu or a confirm window. Close it and re-run.' }
$icon = @(Find-ProcessWindows -ProcessId $appPid -Class $IconClass)
if ($icon.Count -eq 0) { throw "UrlCleaner has no hidden $IconClass window, so there is no NotifyIcon window to post to." }

$area = Get-PrimaryWorkArea
Invoke-WithPointerAt -X ($area.Right - 40) -Y ($area.Bottom - 8) -Do {
    try {
        foreach ($w in $icon) { [void][WinCapture]::PostMessageW($w, $WM_TRAYMOUSEMESSAGE, [IntPtr]::Zero, [IntPtr]$WM_RBUTTONUP) }
        $menu = @()
        for ($i = 0; $i -lt 30 -and $menu.Count -eq 0; $i++) {
            Start-Sleep -Milliseconds 100
            $menu = Get-OpenWindows
        }
        if ($menu.Count -ne 1) { throw "The tray menu did not open: $($menu.Count) UrlCleaner windows visible after 3s." }
        # Past any opening animation, so the two exposures cannot straddle it and
        # record the fade as translucency.
        Start-Sleep -Milliseconds 600
        Invoke-WindowShot @{ Hwnd = $menu[0].ToInt32(); Method = 'Alpha'; Popup = $true; Out = $out }
    } finally {
        foreach ($m in Get-OpenWindows) {
            [void][WinCapture]::PostMessageW($m, $WM_KEYDOWN, [IntPtr]$VK_ESCAPE, [IntPtr]::Zero)
            [void][WinCapture]::PostMessageW($m, $WM_KEYUP, [IntPtr]$VK_ESCAPE, [IntPtr]::Zero)
        }
        $open = @(Wait-MenuClosed)
        if ($open.Count -gt 0) {
            Write-Host 'Escape left the menu open; posting WM_CANCELMODE.'
            foreach ($m in $open) { [void][WinCapture]::PostMessageW($m, $WM_CANCELMODE, [IntPtr]::Zero, [IntPtr]::Zero) }
            $open = @(Wait-MenuClosed)
        }
        if ($open.Count -gt 0) { Write-Warning 'The tray menu is still open after a posted Escape and WM_CANCELMODE; close it by hand.' }
    }
}
