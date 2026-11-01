# Prueba REAL de ventiladores (escribe en el hardware). Copia de la configuracion
# (SPOTLAY_DATA), control activado como en la real, identifica "Salida trasera"
# a 100 % durante 5 s y cierra con --salir (devuelve todo a la BIOS).
$ErrorActionPreference = "Continue"
$out = Join-Path $PSScriptRoot "fan-test"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item (Join-Path $env:LOCALAPPDATA "SpotLay\v11\config.json") (Join-Path $out "config.json")
$env:SPOTLAY_DATA = $out
$exe = "$PSScriptRoot\..\src\SpotLay.App\bin\Debug\net10.0-windows\win-x64\SpotLay.exe"
if (Get-Process SpotLay -ErrorAction SilentlyContinue) { "SpotLay ya abierto: prueba cancelada" | Set-Content "$out\done.txt"; exit }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]; $CT = [System.Windows.Automation.ControlType]
function All($root, $ct) { @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ct)))) }
function Find($root, $like, $ct, $i = 0) { $f = @(All $root $ct | Where-Object { $_.Current.Name -like $like }); if ($f.Count -gt $i) { $f[$i] } else { "NO: $like [$i]" | Add-Content "$out\pasos.txt"; $null } }

$app = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 12
$w = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "SpotLay")))
if ($w) {
    (Find $w "Avanzado" $CT::RadioButton).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep 1
    (Find $w "*Title = Refrigeraci*" $CT::ListItem).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep 3
    $names = @(All $w $CT::Text | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match 'RPM|%|Salida|Frente|Fan' }) -join ' | '
    "estado: $names" | Add-Content "$out\pasos.txt"
    $opts = @(All $w $CT::Button | Where-Object { $_.Current.Name -eq "Opciones" })
    "canales con Opciones: $($opts.Count)" | Add-Content "$out\pasos.txt"
    $opts[$opts.Count - 1].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep 2
    $b = Find $w "Identificar" $CT::Button
    if ($b) { "identificar pulsado $(Get-Date -Format HH:mm:ss)" | Add-Content "$out\pasos.txt"; $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Seconds 9
    (Find $w "Esencial" $CT::RadioButton).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
Start-Process -FilePath $exe -ArgumentList "--salir"
Start-Sleep -Seconds 6
"sigue abierto tras --salir: $([bool](Get-Process -Id $app.Id -ErrorAction SilentlyContinue))" | Add-Content "$out\pasos.txt"
Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
logman stop SpotLay11Etw -ets 2>&1 | Out-Null
"terminado" | Set-Content "$out\done.txt"
