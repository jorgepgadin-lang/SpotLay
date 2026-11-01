# Comprueba la detección de pantalla completa exclusiva (SHQueryUserNotificationState)
# con FakeFgGame en exclusiva (fs), sin bordes (bl) y en ventana.
Add-Type -Namespace T -Name Q -MemberDefinition '[DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int s);'
$exe = Join-Path $PSScriptRoot 'FakeFgGame\FakeFgGame.exe'
foreach ($screen in 'fs', 'bl', '') {
    $p = Start-Process $exe -ArgumentList (@('x3', $screen) | Where-Object { $_ }) -PassThru
    Start-Sleep 4
    $s = 0; [void][T.Q]::SHQueryUserNotificationState([ref]$s)
    "{0,-8} estado={1} exclusiva={2}" -f ($(if ($screen) { $screen } else { 'ventana' })), $s, ($s -eq 3)
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
}
