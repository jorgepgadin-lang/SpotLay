# Ejecuta la sonda del motor contra FakeFgGame. Necesita administrador.
param([string]$Mode = "x3", [int]$Seconds = 6)
$game = Start-Process -FilePath "$PSScriptRoot\FakeFgGame\FakeFgGame.exe" -ArgumentList $Mode -PassThru
Start-Sleep -Milliseconds 800
$probe = "$PSScriptRoot\EngineProbe\bin\Debug\net10.0-windows\win-x64\EngineProbe.exe"
& $probe $game.Id $Seconds "$PSScriptRoot\engine-probe-$Mode.txt"
Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
logman stop SpotLay11Etw -ets 2>&1 | Out-Null
