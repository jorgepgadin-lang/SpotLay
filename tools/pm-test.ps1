# Lanza el PresentMon de SpotLay 6 s contra q2rtx.exe y guarda salida y errores.
$out = Join-Path $PSScriptRoot "pm-test"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Remove-Item "$out\*" -ErrorAction SilentlyContinue
$pm = Join-Path $env:LOCALAPPDATA "SpotLay\v11\tools\PresentMon.exe"
$t = 0
while (-not (Get-Process q2rtx -ErrorAction SilentlyContinue) -and $t -lt 120) { Start-Sleep 1; $t++ }
if (-not (Get-Process q2rtx -ErrorAction SilentlyContinue)) { "q2rtx no se abrio" | Set-Content "$out\done.txt"; exit }
Start-Sleep -Seconds 15
$game = Get-Process q2rtx | Select-Object -First 1
$p = Start-Process -FilePath $pm -ArgumentList "--process_id $($game.Id) --output_stdout --no_console_stats --session_name SpotLayPmTest --stop_existing_session" `
     -RedirectStandardOutput "$out\stdout.csv" -RedirectStandardError "$out\stderr.txt" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 6
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
logman stop SpotLayPmTest -ets 2>&1 | Out-Null
# Tambien sin filtrar por proceso, por si el juego presenta desde otro proceso.
$p = Start-Process -FilePath $pm -ArgumentList "--output_stdout --no_console_stats --session_name SpotLayPmTest --stop_existing_session" `
     -RedirectStandardOutput "$out\todos.csv" -RedirectStandardError "$out\stderr2.txt" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 4
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
logman stop SpotLayPmTest -ets 2>&1 | Out-Null
$mods = ($game.Modules | Where-Object { $_.ModuleName -match 'vulkan|opengl|dxgi|d3d' } | ForEach-Object ModuleName) -join ', '
"modulos: $mods" | Set-Content "$out\modulos.txt"
Stop-Process -Name q2rtx -Force -ErrorAction SilentlyContinue
"pid juego: $($game.Id)" | Set-Content "$out\done.txt"
