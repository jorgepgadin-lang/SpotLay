# Prueba de la fase 2 (alertas, apagado, refrigeracion) sin riesgo:
#  - usa una COPIA de la configuracion (SPOTLAY_DATA = tools\testdata), nunca la real;
#  - --sin-ventiladores: lee los canales pero no escribe en ninguno;
#  - --simular-apagado: la cuenta atras nunca apaga Windows.
# Necesita administrador (SpotLay lo exige y UI Automation debe tener el mismo nivel).
param([string]$Exe = "$PSScriptRoot\..\src\SpotLay.App\bin\Debug\net10.0-windows\win-x64\SpotLay.exe")

$ErrorActionPreference = "Continue"
$shots = Join-Path $PSScriptRoot "shots-perf"
New-Item -ItemType Directory -Force -Path $shots | Out-Null
Remove-Item "$shots\*" -ErrorAction SilentlyContinue

# Copia de la configuracion real (solo lectura del original).
$data = Join-Path $PSScriptRoot "testdata"
Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $data | Out-Null
$real = Join-Path $env:LOCALAPPDATA "SpotLay\v11\config.json"
if (Test-Path $real) { Copy-Item $real (Join-Path $data "config.json") }
$env:SPOTLAY_DATA = $data

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System.Runtime.InteropServices;
public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[Dpi]::SetProcessDPIAware() | Out-Null
$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]

function Note([string]$text) { $text | Add-Content "$shots\pasos.txt" }

function Shot([string]$name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $half = New-Object System.Drawing.Bitmap $bmp, ([int]($bounds.Width / 2)), ([int]($bounds.Height / 2))
    $half.Save((Join-Path $shots "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $half.Dispose()
    Note "captura $name"
}

function All($root, $controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType)
    return @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond))
}

function Find($root, [string]$nameLike, $controlType, [int]$index = 0) {
    $found = @(All $root $controlType | Where-Object { $_.Current.Name -like $nameLike })
    if ($found.Count -gt $index) { return $found[$index] }
    Note "NO ENCONTRADO: $nameLike [$index]"
    return $null
}

function Select-Nav($window, [string]$title) {
    $item = Find $window "*Title = $title,*" $CT::ListItem
    if ($item) { $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 1500 }
}

function Click-Radio($window, [string]$name) {
    $r = Find $window $name $CT::RadioButton
    if ($r) { $r.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 1200 }
}

function Click-Button($window, [string]$name, [int]$index = 0) {
    $b = Find $window $name $CT::Button $index
    if ($b) { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800 }
}

function Set-Check($window, [string]$name, [int]$index, [bool]$on) {
    $c = Find $window $name $CT::CheckBox $index
    if (-not $c) { return }
    $toggle = $c.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $isOn = $toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
    if ($isOn -ne $on) { $toggle.Toggle() }
    Start-Sleep -Milliseconds 500
}

# Pone texto en la caja N con ese tooltip y quita el foco para que se guarde.
function Set-Box($window, [string]$help, [int]$index, [string]$text) {
    $boxes = @(All $window $CT::Edit | Where-Object { $_.Current.HelpText -eq $help })
    if ($boxes.Count -le $index) { Note "NO ENCONTRADA caja '$help' [$index]"; return }
    $box = $boxes[$index]
    $box.SetFocus()
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    $nav = Find $window "*Title = Alertas,*" $CT::ListItem
    if ($nav) { $nav.SetFocus() }
    Start-Sleep -Milliseconds 600
}

function Window-Exists([string]$title) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $title)
    return $null -ne $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}


if (Get-Process SpotLay -ErrorAction SilentlyContinue) { Start-Process -FilePath $Exe -ArgumentList "--salir"; Start-Sleep 6 }
$game = Start-Process -FilePath "$PSScriptRoot\FakeFgGame\FakeFgGame.exe" -ArgumentList "x3" -PassThru
Start-Sleep -Milliseconds 800
$app = Start-Process -FilePath $Exe -ArgumentList "--pid", $game.Id, "--sin-ventiladores" -PassThru
Start-Sleep -Seconds 14
$window = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "SpotLay")))
if ($window) { Select-Nav $window "Rendimiento"; Start-Sleep 3; Shot "01-rendimiento"; Select-Nav $window "Ajustes"; Start-Sleep 1; Shot "02-ajustes" } else { Note "sin ventana" }
Start-Process -FilePath $Exe -ArgumentList "--salir"; Start-Sleep 5
Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
logman stop SpotLay11Etw -ets 2>&1 | Out-Null
Get-Process PresentMon -ErrorAction SilentlyContinue | Stop-Process -Force
logman stop SpotLay11Vk -ets 2>&1 | Out-Null
"terminado" | Set-Content "$shots\done.txt"
