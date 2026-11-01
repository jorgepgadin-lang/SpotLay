# Prueba de interfaz: arranca SpotLay contra FakeFgGame (x3), recorre las
# pantallas con UI Automation y guarda capturas en tools\shots. Necesita
# administrador (SpotLay lo exige y UI Automation debe tener el mismo nivel).
param([string]$Exe = "$PSScriptRoot\..\src\SpotLay.App\bin\Debug\net10.0-windows\win-x64\SpotLay.exe",
      [switch]$Import)

$ErrorActionPreference = "Continue"
$shots = Join-Path $PSScriptRoot "shots"
New-Item -ItemType Directory -Force -Path $shots | Out-Null
Remove-Item "$shots\*.png" -ErrorAction SilentlyContinue

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System.Runtime.InteropServices;
public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[Dpi]::SetProcessDPIAware() | Out-Null

function Shot([string]$name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $half = New-Object System.Drawing.Bitmap $bmp, ([int]($bounds.Width / 2)), ([int]($bounds.Height / 2))
    $half.Save((Join-Path $shots "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $half.Dispose()
}
Add-Type -AssemblyName System.Windows.Forms

function Find([System.Windows.Automation.AutomationElement]$root, [string]$nameContains, $controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)
    foreach ($e in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($e.Current.Name -like "*$nameContains*") { return $e }
    }
    return $null
}

function Select-Nav($window, [string]$title) {
    $item = Find $window "Title = $title," ([System.Windows.Automation.ControlType]::ListItem)
    if ($item) { $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 1200 }
    else { "no encontrado: $title" | Add-Content "$shots\errores.txt" }
}

function Click-Radio($window, [string]$name) {
    $r = Find $window $name ([System.Windows.Automation.ControlType]::RadioButton)
    if ($r) { $r.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 1200 }
    else { "no encontrado: $name" | Add-Content "$shots\errores.txt" }
}

function Click-Button($window, [string]$name) {
    $b = Find $window $name ([System.Windows.Automation.ControlType]::Button)
    if ($b) { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1500 }
    else { "no encontrado: $name" | Add-Content "$shots\errores.txt" }
}

$game = Start-Process -FilePath "$PSScriptRoot\FakeFgGame\FakeFgGame.exe" -ArgumentList "x3" -PassThru
Start-Sleep -Milliseconds 800
$app = Start-Process -FilePath $Exe -ArgumentList "--pid", $game.Id -PassThru
Start-Sleep -Seconds 8

$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "SpotLay")
$window = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $window) { "ventana SpotLay no encontrada" | Add-Content "$shots\errores.txt" }
else {
    Shot "01-inicio"
    if ($Import) { Click-Button $window "Importar"; Shot "02-inicio-importado" }
    Select-Nav $window "Overlay"; Shot "03-overlay-esencial"
    Click-Radio $window "Avanzado"; Shot "04-overlay-avanzado"
    Select-Nav $window "M*tricas"; Shot "05-metricas-avanzado"
    Click-Radio $window "Esencial"; Shot "06-metricas-esencial"
    Select-Nav $window "Perfiles"; Shot "07-perfiles"
    Select-Nav $window "Ajustes"; Shot "08-ajustes"
    Select-Nav $window "Inicio"; Start-Sleep -Seconds 2; Shot "09-inicio-final"
}

Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
Get-Process PresentMon -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
logman stop SpotLay11Etw -ets 2>&1 | Out-Null
logman stop SpotLay11Vk -ets 2>&1 | Out-Null
"terminado" | Set-Content "$shots\done.txt"
