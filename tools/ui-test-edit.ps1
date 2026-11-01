# Prueba de la fase 2 (alertas, apagado, refrigeracion) sin riesgo:
#  - usa una COPIA de la configuracion (SPOTLAY_DATA = tools\testdata), nunca la real;
#  - --sin-ventiladores: lee los canales pero no escribe en ninguno;
#  - --simular-apagado: la cuenta atras nunca apaga Windows.
# Necesita administrador (SpotLay lo exige y UI Automation debe tener el mismo nivel).
param([string]$Exe = "$PSScriptRoot\..\src\SpotLay.App\bin\Debug\net10.0-windows\win-x64\SpotLay.exe")

$ErrorActionPreference = "Continue"
$shots = Join-Path $PSScriptRoot "shots-edit"
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
# --- Modo edición del overlay real + estilo por métrica ---
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Fg {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
"@
function Front([IntPtr]$h) { KeyDown 0x12; [Fg]::ShowWindow($h, 9) | Out-Null; [Fg]::SetForegroundWindow($h) | Out-Null; KeyUp 0x12; Start-Sleep -Seconds 3 }
function OverlayRect {
    $o = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "SpotLay Overlay")))
    if ($o) { $r = $o.Current.BoundingRectangle; "x=$($r.X) y=$($r.Y) w=$($r.Width) h=$($r.Height)" } else { "overlay no encontrado" }
}
# Recorte a resolución real del overlay (para ver si las cifras caben en los arcos).
function ShotOverlay([string]$name) {
    $o = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "SpotLay Overlay")))
    if (-not $o) { Note "sin overlay para $name"; return }
    $r = $o.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $shots "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Note "recorte $name"
}
# La lista de métricas virtualiza: se busca con ItemContainerPattern y se materializa.
function Select-Metric($window, [string]$label, [bool]$add) {
    $list = @(All $window $CT::List | Where-Object { $_.Current.AutomationId -eq "MetricList" })[0]
    if (-not $list) { Note "NO ENCONTRADA lista de métricas"; return }
    $ic = $list.GetCurrentPattern([System.Windows.Automation.ItemContainerPattern]::Pattern)
    $item = $null; $prev = $null
    while ($true) {
        $prev = $ic.FindItemByProperty($prev, $null, $null)
        if (-not $prev) { break }
        if ($prev.Current.Name -like "*Label = $label,*") { $item = $prev; break }
    }
    if (-not $item) { Note "NO ENCONTRADA métrica $label"; return }
    try { $item.GetCurrentPattern([System.Windows.Automation.VirtualizedItemPattern]::Pattern).Realize() } catch {}
    Start-Sleep -Milliseconds 300
    $p = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($add) { $p.AddToSelection() } else { $p.Select() }
    Note "métrica $label seleccionada (add=$add)"
    Start-Sleep -Milliseconds 700
}
function Pick-Combo($window, [string]$name, [string]$option) {
    $combo = Find $window $name $CT::ComboBox
    if (-not $combo) { return }
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 500
    $opt = Find $combo "*Label = $option*" $CT::ListItem
    if ($opt) { $opt.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Note "combo $name = $option" }
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 800
}

if (Get-Process SpotLay -ErrorAction SilentlyContinue) { Start-Process -FilePath $Exe -ArgumentList "--salir"; Start-Sleep 6 }
$game = Start-Process -FilePath "$PSScriptRoot\FakeFgGame\FakeFgGame.exe" -ArgumentList "x3" -PassThru
Start-Sleep -Seconds 2
$app = Start-Process -FilePath $Exe -ArgumentList "--sin-ventiladores" -PassThru
Start-Sleep -Seconds 8
$window = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "SpotLay")))
$main = [IntPtr]$window.Current.NativeWindowHandle
Click-Radio $window "Avanzado"
Select-Nav $window "Inicio"

# 1) Jugando: juego delante unos segundos (se guardan FPS/FG/frame time).
Front $game.MainWindowHandle
Start-Sleep -Seconds 4
Note ("1 juego delante: " + (OverlayRect)); Shot "01-juego"

# 2) Alt+Tab a SpotLay en Inicio: overlay normal (no juego).
Front $main
Note ("2 SpotLay en Inicio: " + (OverlayRect)); Shot "02-inicio"

# 3) Página Overlay: modo edición (diseño que se edita, métricas del juego con el último valor, marco).
Select-Nav $window "Overlay"
Start-Sleep -Seconds 2
Note ("3 SpotLay en Overlay: " + (OverlayRect)); Shot "03-editando"; ShotOverlay "03b-overlay"

# 4) Copiar estilo de CPU Temp y pegarlo solo en CPU Fan.
Select-Metric $window "CPU Temp" $false
# Valor naranja (2.º grupo de muestras = color del valor de la métrica) y unidad igual que el valor.
$sw = @(All $window $CT::Button | Where-Object { $_.Current.HelpText -eq "#FFB547" })
Note "muestras naranja: $($sw.Count)"
if ($sw.Count -ge 2) { $sw[1].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 600 }
Click-Button $window "Igual que el valor"
Click-Button $window "Copiar estilo"
Select-Metric $window "CPU Fan" $false
Click-Button $window "Pegar estilo"
Shot "04-pegado-cpu-fan"; ShotOverlay "04b-overlay"

# 5) Varias a la vez: GPU Fan + CPU Fan, unidad "Debajo".
Select-Metric $window "GPU Fan" $false
Select-Metric $window "CPU Fan" $true
Pick-Combo $window "Posición de la unidad" "Debajo"
Shot "05-varias-debajo"; ShotOverlay "05b-overlay"
Pick-Combo $window "Posición de la unidad" "Automática"

# 6) Minimizar SpotLay: fin del modo edición.
[Fg]::ShowWindow($main, 6) | Out-Null; Start-Sleep -Seconds 3
Note ("6 SpotLay minimizado: " + (OverlayRect)); Shot "06-minimizado"

# 7) Volver al juego.
Front $game.MainWindowHandle
Note ("7 juego otra vez: " + (OverlayRect)); Shot "07-juego"

Start-Process -FilePath $Exe -ArgumentList "--salir"; Start-Sleep 5
Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
logman stop SpotLay11Etw -ets 2>&1 | Out-Null
# B) Sin juego: overlay de edición con valores de ejemplo ("x4 DINÁMICO").
$app = Start-Process -FilePath $Exe -ArgumentList "--sin-ventiladores" -PassThru
Start-Sleep -Seconds 8
$window = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "SpotLay")))
Select-Nav $window "Overlay"
Start-Sleep -Seconds 2
Shot "08-sin-juego"; ShotOverlay "08b-overlay"
Start-Process -FilePath $Exe -ArgumentList "--salir"; Start-Sleep 5
Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $data "config.json") (Join-Path $shots "config-final.json") -ErrorAction SilentlyContinue
Copy-Item (Join-Path $data "logs\spotlay.log") (Join-Path $shots "spotlay.log") -ErrorAction SilentlyContinue
"terminado" | Set-Content "$shots\done.txt"
