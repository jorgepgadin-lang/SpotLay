# Prueba de la fase 2 (alertas, apagado, refrigeracion) sin riesgo:
#  - usa una COPIA de la configuracion (SPOTLAY_DATA = tools\testdata), nunca la real;
#  - --sin-ventiladores: lee los canales pero no escribe en ninguno;
#  - --simular-apagado: la cuenta atras nunca apaga Windows.
# Necesita administrador (SpotLay lo exige y UI Automation debe tener el mismo nivel).
param([string]$Exe = "$PSScriptRoot\..\src\SpotLay.App\bin\Debug\net10.0-windows\win-x64\SpotLay.exe")

$ErrorActionPreference = "Continue"
$shots = Join-Path $PSScriptRoot "shots-fase2"
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

$game = Start-Process -FilePath "$PSScriptRoot\FakeFgGame\FakeFgGame.exe" -ArgumentList "x3" -PassThru
Start-Sleep -Milliseconds 800
$app = Start-Process -FilePath $Exe -ArgumentList "--pid", $game.Id, "--sin-ventiladores", "--simular-apagado" -PassThru
Start-Sleep -Seconds 9

$cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "SpotLay")
$window = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $window) { Note "ventana SpotLay no encontrada" }
else {
    Click-Radio $window "Esencial"

    # --- Alertas (Esencial) ---
    Select-Nav $window "Alertas"; Shot "01-alertas-esencial"

    Click-Button $window "Probar aviso" 3          # CPU Temp
    Start-Sleep -Milliseconds 700
    Note ("aviso de prueba visible: " + (Window-Exists "SpotLay aviso"))
    Shot "02-aviso-de-prueba"

    Click-Button $window "Probar cuenta*"
    Start-Sleep -Seconds 2
    Note ("ventana de apagado visible: " + (Window-Exists "SpotLay apagado de emergencia"))
    Shot "03-cuenta-atras-simulada"
    [System.Windows.Forms.SendKeys]::SendWait("^%{F9}")   # Ctrl+Alt+F9
    Note ("ventana de apagado tras la tecla: " + (Window-Exists "SpotLay apagado de emergencia"))
    Start-Sleep -Seconds 1
    Shot "04-tras-ctrl-alt-f9"

    Click-Button $window "Probar cuenta*"
    Start-Sleep -Seconds 12
    Shot "05-simulacion-llega-a-cero"
    Start-Sleep -Seconds 6

    # --- Apagado por sensor (modo prueba): CPU Temp con limite 20 ---
    Set-Box $window "Límite del apagado de emergencia" 3 "20"
    Set-Check $window "Apagado" 3 $true
    Start-Sleep -Seconds 3
    Shot "06-cuenta-atras-por-sensor"
    Set-Check $window "Apagado" 3 $false
    Start-Sleep -Seconds 2
    Shot "07-desarmado-desde-panel"

    # --- Aviso por sensor: CPU Temp con limite 20 (ventana + overlay en rojo) ---
    Set-Box $window "Límite del aviso" 3 "20"
    Set-Check $window "Aviso" 3 $true
    Start-Sleep -Seconds 3
    Shot "08-aviso-por-sensor"
    Set-Check $window "Aviso" 3 $false
    Start-Sleep -Seconds 2

    # --- Alertas (Avanzado) ---
    Click-Radio $window "Avanzado"
    Select-Nav $window "Alertas"
    Click-Button $window "Opciones" 3
    Start-Sleep -Seconds 1
    Shot "09-alertas-avanzado"

    # --- Refrigeracion ---
    Click-Radio $window "Esencial"
    Select-Nav $window "Refrigeraci*n"; Start-Sleep -Seconds 2; Shot "10-refrigeracion-esencial"
    Click-Radio $window "Avanzado"
    Select-Nav $window "Refrigeraci*n"
    Click-Button $window "Opciones" 0
    Start-Sleep -Seconds 2
    Shot "11-refrigeracion-avanzado"
    Click-Radio $window "Esencial"
}

Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
Get-Process PresentMon -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
logman stop SpotLay11Etw -ets 2>&1 | Out-Null
logman stop SpotLay11Vk -ets 2>&1 | Out-Null
Copy-Item (Join-Path $data "logs\spotlay.log") (Join-Path $shots "spotlay.log") -ErrorAction SilentlyContinue
Copy-Item (Join-Path $data "config.json") (Join-Path $shots "config-final.json") -ErrorAction SilentlyContinue
"terminado" | Set-Content "$shots\done.txt"
