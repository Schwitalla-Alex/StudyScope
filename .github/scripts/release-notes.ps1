# Erstellt den Text des GitHub-Releases: Abschnitt aus CHANGELOG.md, Installationshinweise (inkl. SmartScreen,
# ffmpeg optional) und eine Tabelle mit Größe und SHA-256 der Pakete. Schreibt außerdem SHA256SUMS.txt in den Paketordner.
# Aufruf: pwsh .github/scripts/release-notes.ps1 -Version 1.1.0 -PackageDirectory dist -OutFile release-notes.md
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$OutFile
)

$ErrorActionPreference = "Stop"
$setup = "StudyScope-$Version-Setup.exe"
$linux = "StudyScope-$Version-linux-x64.tar.gz"
$folder = "StudyScope-$Version-linux-x64"

foreach ($name in @($setup, $linux)) {
    if (-not (Test-Path -LiteralPath (Join-Path $PackageDirectory $name))) {
        throw "Paket fehlt: $name (vorhanden: $((Get-ChildItem $PackageDirectory -File).Name -join ', '))"
    }
}

$changelog = & (Join-Path $PSScriptRoot "changelog-section.ps1") -Version $Version
if ($LASTEXITCODE -ne 0) { throw "CHANGELOG.md enthält keinen Abschnitt für $Version." }

$de = [System.Globalization.CultureInfo]::GetCultureInfo("de-DE")
$rows = [System.Collections.Generic.List[string]]::new()
$sums = [System.Collections.Generic.List[string]]::new()
foreach ($name in @($setup, $linux)) {
    $file = Get-Item -LiteralPath (Join-Path $PackageDirectory $name)
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = ($file.Length / 1MB).ToString("N1", $de)
    $rows.Add("| ``$name`` | $size MB | ``$hash`` |")
    $sums.Add("$hash  $name")
}
# Format wie sha256sum: „<hash>  <datei>“, LF – damit funktioniert „sha256sum -c SHA256SUMS.txt“
[System.IO.File]::WriteAllText((Join-Path $PackageDirectory "SHA256SUMS.txt"), ($sums -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))

$notes = @"
$changelog

### Installation
- **Windows:** ``$setup`` herunterladen und starten. Voraussetzung ist die WebView2-Runtime (unter Windows 11 vorinstalliert).
  - Die setup.exe ist **nicht digital signiert**. Windows SmartScreen zeigt deshalb möglicherweise „Der Computer wurde durch Windows geschützt“. Dann auf **Weitere Informationen** und anschließend auf **Trotzdem ausführen** klicken.
- **Linux (x86_64):** Erforderlich ist **WebKitGTK 4.1** (``libwebkit2gtk-4.1``, z. B. ab Ubuntu 22.04 / Debian 12). ``install.sh`` prüft die Systembibliotheken und nennt bei Bedarf den passenden Installationsbefehl. Das Archiv entpackt sich in den Ordner ``$folder``:
  ``````bash
  tar -xzf $linux
  cd $folder
  bash install.sh
  ``````
- **Optional – ffmpeg:** Nur für den Export von Videos als **MP4** und von Audio als **MP3** nötig, alles andere funktioniert ohne. Windows: ``winget install Gyan.FFmpeg`` · Linux: ``sudo apt install ffmpeg``.

### Prüfsummen (SHA-256)
| Datei | Größe | SHA-256 |
|---|---|---|
$($rows -join "`n")

So prüfen Sie, dass die heruntergeladene Datei unverändert ist – die angezeigte Prüfsumme muss mit der Tabelle übereinstimmen:
- **Windows (PowerShell)** im Download-Ordner: ``Get-FileHash .\$setup -Algorithm SHA256``
- **Linux:** ``sha256sum $linux`` – oder alle auf einmal mit der Datei ``SHA256SUMS.txt`` aus diesem Release (im selben Ordner): ``sha256sum -c --ignore-missing SHA256SUMS.txt``
"@

[System.IO.File]::WriteAllText($OutFile, $notes.Replace("`r`n", "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "Release-Text geschrieben: $OutFile"
