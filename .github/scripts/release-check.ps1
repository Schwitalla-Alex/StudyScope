# Prüft vor einem Release, ob alles zusammenpasst – sonst Abbruch mit klarer Meldung:
#   1. Tag hat die Form v<Major>.<Minor>.<Patch>
#   2. Tag-Version = <Version> in Directory.Build.props
#   3. Der Tag zeigt auf einen Commit, der in master enthalten ist
#   4. CHANGELOG.md enthält einen Abschnitt „## [<Version>]“
# Aufruf: pwsh .github/scripts/release-check.ps1 -Tag v1.1.0 [-Commit <sha>] [-SkipMasterCheck]
# Schreibt bei Erfolg „version=<Version>“ nach $GITHUB_OUTPUT (falls gesetzt).
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$Commit = "HEAD",
    [switch]$SkipMasterCheck
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Fail([string]$message) {
    if ($env:GITHUB_ACTIONS) { Write-Output "::error title=Release abgebrochen::$message" }
    [Console]::Error.WriteLine("FEHLER: $message")
    exit 1
}

if ($Tag -notmatch '^v(\d+\.\d+\.\d+)$') {
    Fail "Der Tag '$Tag' hat nicht die Form v<Major>.<Minor>.<Patch> (z. B. v1.1.0)."
}
$tagVersion = $Matches[1]

[xml]$props = Get-Content -LiteralPath (Join-Path $root "Directory.Build.props") -Raw -Encoding UTF8
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { Fail "In Directory.Build.props wurde keine <Version> gefunden." }
$version = $version.Trim()
if ($version -ne $tagVersion) {
    Fail "Tag und Version passen nicht zusammen: Tag '$Tag', Directory.Build.props enthält '$version'. Entweder die Version in Directory.Build.props anheben (per Pull Request) oder den Tag v$version setzen. Den falschen Tag löschen mit: git push --delete origin $Tag"
}

if (-not $SkipMasterCheck) {
    & git -C $root merge-base --is-ancestor $Commit origin/master
    if ($LASTEXITCODE -ne 0) {
        Fail "Der Tag '$Tag' zeigt auf einen Commit, der nicht in master enthalten ist. Releases nur von master taggen (erst den Pull Request mergen, dann auf master taggen)."
    }
}

& (Join-Path $PSScriptRoot "changelog-section.ps1") -Version $version | Out-Null
if ($LASTEXITCODE -ne 0) {
    Fail "CHANGELOG.md enthält keinen Abschnitt '## [$version]'. Bitte die Änderungen dieser Version dort eintragen (per Pull Request)."
}

Write-Output "Release-Prüfung bestanden: Tag $Tag, Version $version, CHANGELOG-Abschnitt vorhanden."
if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "version=$version" -Encoding UTF8 }
exit 0
