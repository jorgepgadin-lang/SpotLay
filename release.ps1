# Publica una versión nueva en GitHub (las copias de SpotLay la instalan solas).
#   .\release.ps1 12.1.1
# 1) pone la versión en el proyecto, 2) genera publish\SpotLay.exe, 3) calcula su SHA-256,
# 4) crea la Release "v12.1.1" en jorgepgadin-lang/SpotLay con el exe, la huella y las
#    notas de esa versión sacadas de ..\SpotLay-github\CHANGELOG.md.
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$repo = "jorgepgadin-lang/SpotLay"
$docs = Join-Path $PSScriptRoot "..\SpotLay-github"
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = "C:\Program Files\GitHub CLI\gh.exe" }

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "La versión tiene que ser X.Y.Z (por ejemplo 12.1.1)" }
# Solo estorba el SpotLay abierto desde publish\ (bloquea el exe); el instalado en otra carpeta puede seguir abierto.
$publishExe = Join-Path $PSScriptRoot "publish\SpotLay.exe"
if (Get-CimInstance Win32_Process -Filter "Name='SpotLay.exe'" | Where-Object { $_.ExecutablePath -eq $publishExe }) {
    throw "Cierra el SpotLay abierto desde publish\ antes de publicar"
}

# Notas de la versión: la sección "## X.Y.Z" del CHANGELOG hasta la siguiente "## ".
$changelog = Get-Content (Join-Path $docs "CHANGELOG.md") -Raw -Encoding UTF8
$match = [regex]::Match($changelog, "(?ms)^## $([regex]::Escape($Version))\b.*?(?=^## |\z)")
if (-not $match.Success) { throw "Falta la sección '## $Version' en SpotLay-github\CHANGELOG.md" }
$notes = Join-Path $env:TEMP "spotlay-notes-$Version.md"
[IO.File]::WriteAllText($notes, ($match.Value -replace "(?m)^## .*\r?\n", "").Trim(), [Text.UTF8Encoding]::new($false))

# Versión en el proyecto.
$csproj = "src\SpotLay.App\SpotLay.App.csproj"
$text = Get-Content $csproj -Raw -Encoding UTF8
$text = [regex]::Replace($text, "<Version>[^<]*</Version>", "<Version>$Version</Version>")
[IO.File]::WriteAllText((Resolve-Path $csproj), $text, [Text.UTF8Encoding]::new($false))

& .\publish.ps1
$exe = "publish\SpotLay.exe"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$sha = "publish\SpotLay.exe.sha256"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot $sha), "$hash  SpotLay.exe`n", [Text.UTF8Encoding]::new($false))
Write-Host "SHA-256 $hash"

& $gh release create "v$Version" $exe $sha --repo $repo --title "SpotLay $Version" --notes-file $notes
if ($LASTEXITCODE -ne 0) { throw "gh release create falló" }

# Solo se guardan las 10 últimas versiones: el actualizador solo mira la última.
$keep = 10
# (En Windows PowerShell la lista llega como un solo objeto: se guarda antes de recorrerla.)
$all = & $gh release list --repo $repo --limit 100 --json tagName,publishedAt | Out-String | ConvertFrom-Json
$old = $all | Sort-Object { [datetime]$_.publishedAt } -Descending | Select-Object -Skip $keep
foreach ($r in $old) {
    & $gh release delete $r.tagName --repo $repo --yes --cleanup-tag
    Write-Host "Borrada la versión antigua $($r.tagName)"
}
Write-Host "Publicada SpotLay ${Version}: https://github.com/$repo/releases/tag/v$Version"

