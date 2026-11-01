# Prueba tres formas de activar el proveedor DXGI. Necesita administrador.
$out = "$PSScriptRoot\engine-probe-variants.txt"
"otros procesos de captura: " + ((Get-Process PresentMon*, SpotLay*, FgMult* -ErrorAction SilentlyContinue | ForEach-Object { $_.ProcessName }) -join ", ") | Set-Content $out
"sesiones ETW activas: " + ((logman query -ets | Select-String -Pattern "PresentMon|SpotLay|FgMult" | ForEach-Object { $_.Line.Trim() }) -join " | ") | Add-Content $out
foreach ($v in @(@{L="4";A="0"}, @{L="0";A="1"}, @{L="5";A="0"}, @{L="5";A="1"})) {
    $env:SPOTLAY_DXGI_LEVEL = $v.L
    $env:SPOTLAY_DXGI_ALL = $v.A
    $game = Start-Process -FilePath "$PSScriptRoot\FakeFgGame\FakeFgGame.exe" -ArgumentList "x3" -PassThru
    Start-Sleep -Milliseconds 800
    & "$PSScriptRoot\EngineProbe\bin\Debug\net10.0-windows\win-x64\EngineProbe.exe" $game.Id 3 "$PSScriptRoot\probe-tmp.txt"
    Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
    logman stop SpotLay11Etw -ets 2>&1 | Out-Null
    "=== nivel=$($v.L) matchAll=$($v.A) ===" | Add-Content $out
    Get-Content "$PSScriptRoot\probe-tmp.txt" | Select-Object -Last 2 | Add-Content $out
}
Remove-Item "$PSScriptRoot\probe-tmp.txt" -ErrorAction SilentlyContinue
