param(
    [Parameter(Mandatory = $true)][int]$FromX,
    [Parameter(Mandatory = $true)][int]$FromY,
    [Parameter(Mandatory = $true)][int]$ToX,
    [Parameter(Mandatory = $true)][int]$ToY,
    [int]$Steps = 24,
    [int]$StepMs = 25
)

# Synthetic left-button drag using the real Win32 input queue, so the
# application sees genuine WM_MOUSEMOVE / WM_LBUTTONDOWN / WM_LBUTTONUP messages.

Add-Type -Name M -Namespace ZDrag -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, System.IntPtr extra);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
'@

[void][ZDrag.M]::SetProcessDPIAware()

$LEFTDOWN = 0x0002
$LEFTUP = 0x0004

[void][ZDrag.M]::SetCursorPos($FromX, $FromY)
Start-Sleep -Milliseconds 220

[ZDrag.M]::mouse_event($LEFTDOWN, 0, 0, 0, [System.IntPtr]::Zero)
Start-Sleep -Milliseconds 120

for ($i = 1; $i -le $Steps; $i++) {
    $x = [int]($FromX + ($ToX - $FromX) * $i / $Steps)
    $y = [int]($FromY + ($ToY - $FromY) * $i / $Steps)
    [void][ZDrag.M]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds $StepMs
}

Start-Sleep -Milliseconds 120
[ZDrag.M]::mouse_event($LEFTUP, 0, 0, 0, [System.IntPtr]::Zero)
Start-Sleep -Milliseconds 120

"drag $FromX,$FromY -> $ToX,$ToY done"
