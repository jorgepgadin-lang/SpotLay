# Prueba del TOTAL con Present en rafagas (x6 a 19,7 reales, como Crimson Desert)
# y del reinicio automatico de la sesion ETW. Datos de prueba aparte, sin ventiladores.
$ErrorActionPreference = "Continue"
$out = Join-Path $PSScriptRoot "fg-burst"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $out | Out-Null
$game = Join-Path $PSScriptRoot "FakeFgGame\FakeFgGame.exe"
$runs = @(
    
    @{ Name = "despues"; Exe = "$PSScriptRoot\..\src\SpotLay.App\bin\Debug\net10.0-windows\win-x64\SpotLay.exe"; KillAt = 12 }
)
foreach ($r in $runs) {
    $data = Join-Path $out $r.Name
    New-Item -ItemType Directory -Force -Path $data | Out-Null
    $env:SPOTLAY_DATA = $data
    $g = Start-Process -FilePath $game -ArgumentList "burst6" -PassThru
    Start-Sleep -Milliseconds 800
    $a = Start-Process -FilePath $r.Exe -ArgumentList "--pid", $g.Id, "--sin-ventiladores", "--minimized" -PassThru
    if ($r.KillAt -gt 0) {
        Start-Sleep -Seconds $r.KillAt
        "$(Get-Date -Format HH:mm:ss) sesion parada a mano" | Add-Content "$out\pasos.txt"
        logman stop SpotLay11Etw -ets 2>&1 | Out-Null
        Start-Sleep -Seconds 30
    } else { Start-Sleep -Seconds 16 }
    Stop-Process -Id $a.Id -Force -ErrorAction SilentlyContinue
    Stop-Process -Id $g.Id -Force -ErrorAction SilentlyContinue
    logman stop SpotLay11Etw -ets 2>&1 | Out-Null
    Start-Sleep -Seconds 1
}
"terminado" | Set-Content "$out\done.txt"
