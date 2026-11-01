# Resumen de SpotLay en GitHub: descargas por versión, interés, visitas e incidencias.
#   .\stats.ps1
$ErrorActionPreference = "Stop"
$repo = "jorgepgadin-lang/SpotLay"
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = "C:\Program Files\GitHub CLI\gh.exe" }

function Api([string]$path) { & $gh api $path | ConvertFrom-Json }

$info = Api "repos/$repo"
Write-Host ""
Write-Host "SpotLay en GitHub  ·  https://github.com/$repo" -ForegroundColor Cyan
Write-Host ("Estrellas: {0}   Siguiendo: {1}   Copias (forks): {2}   Incidencias abiertas: {3}" -f `
    $info.stargazers_count, $info.subscribers_count, $info.forks_count, $info.open_issues_count)

Write-Host ""
Write-Host "Descargas de SpotLay.exe por versión (incluye las actualizaciones automáticas):" -ForegroundColor Cyan
$total = 0
foreach ($release in (Api "repos/$repo/releases")) {
    $exe = $release.assets | Where-Object name -eq "SpotLay.exe"
    $count = if ($exe) { $exe.download_count } else { 0 }
    $total += $count
    Write-Host ("  {0,-10} {1,6}   publicada {2:dd/MM/yyyy}" -f $release.tag_name, $count, [datetime]$release.published_at)
}
Write-Host ("  {0,-10} {1,6}" -f "TOTAL", $total)

Write-Host ""
Write-Host "Visitas a la página (últimos 14 días):" -ForegroundColor Cyan
try {
    $views = Api "repos/$repo/traffic/views"
    Write-Host ("  {0} visitas · {1} personas distintas" -f $views.count, $views.uniques)
    $refs = Api "repos/$repo/traffic/popular/referrers"
    foreach ($r in $refs) { Write-Host ("  desde {0}: {1} visitas ({2} personas)" -f $r.referrer, $r.count, $r.uniques) }
} catch {
    Write-Host "  (no disponible: $($_.Exception.Message))"
}

Write-Host ""
Write-Host "Incidencias abiertas:" -ForegroundColor Cyan
$issues = & $gh issue list --repo $repo --state open --limit 20 --json number,title,author,createdAt | ConvertFrom-Json
if (-not $issues) { Write-Host "  ninguna" }
foreach ($i in $issues) {
    Write-Host ("  #{0}  {1}  ·  {2}  ·  {3:dd/MM/yyyy}" -f $i.number, $i.title, $i.author.login, [datetime]$i.createdAt)
}
Write-Host ""
