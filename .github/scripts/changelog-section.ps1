# Gibt den Abschnitt einer Version aus CHANGELOG.md aus – ohne die Überschrift „## [x.y.z] …“,
# bis zur nächsten Versionsüberschrift „## [“. Exitcode 1, wenn es keinen (nicht leeren) Abschnitt gibt.
# Aufruf: pwsh .github/scripts/changelog-section.ps1 -Version 1.1.0
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Path = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "CHANGELOG.md")
)

if (-not (Test-Path -LiteralPath $Path)) { exit 1 }
$lines = Get-Content -LiteralPath $Path -Encoding UTF8
$heading = '^##\s+\[' + [regex]::Escape($Version) + '\]'
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $heading) { $start = $i + 1; break }
}
if ($start -lt 0) { exit 1 }

$section = [System.Collections.Generic.List[string]]::new()
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+\[') { break }
    $section.Add($lines[$i])
}
$text = ($section -join "`n").Trim()
if (-not $text) { exit 1 }
Write-Output $text
exit 0
