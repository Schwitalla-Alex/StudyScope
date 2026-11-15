<#
.SYNOPSIS
    Baut das StudyScope-Installationsprogramm (Windows: setup.exe, Linux: tar.gz mit install.sh).

.DESCRIPTION
    1. StudyScope.UI self-contained für die Ziel-Runtime veröffentlichen
    2. Programmdateien als payload.zip packen
    3. StudyScope.Setup als komprimierte Single-File-Anwendung mit eingebettetem Paket veröffentlichen
    4. Auslieferungsdatei nach artifacts/ schreiben

    Beide Runtimes lassen sich unter Windows bauen. Die Version stammt aus Directory.Build.props.

.EXAMPLE
    .\build\build-setup.ps1                 # Windows (win-x64)
    .\build\build-setup.ps1 -Runtime linux-x64
    .\build\build-setup.ps1 -Runtime all
#>
param(
    [ValidateSet("win-x64", "linux-x64", "all")]
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }
}

$version = (& dotnet msbuild (Join-Path $root "StudyScope.UI\StudyScope.UI.csproj") -getProperty:Version).Trim()
if (-not $version) { throw "Version konnte nicht aus Directory.Build.props gelesen werden." }

$runtimes = if ($Runtime -eq "all") { @("win-x64", "linux-x64") } else { @($Runtime) }

foreach ($rid in $runtimes) {
    Write-Host "`n=== StudyScope $version für $rid ===" -ForegroundColor Cyan

    $appDir = Join-Path $artifacts "app\$rid"
    $payload = Join-Path $artifacts "payload\StudyScope-$rid.zip"
    $setupDir = Join-Path $artifacts "setup\$rid"
    foreach ($dir in @($appDir, $setupDir)) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
    New-Item -ItemType Directory -Force (Split-Path $payload) | Out-Null

    Write-Host "[1/4] StudyScope.UI veröffentlichen"
    Invoke-Dotnet publish (Join-Path $root "StudyScope.UI\StudyScope.UI.csproj") -c $Configuration -r $rid --self-contained `
        -p:DebugType=none -o $appDir --nologo -v quiet

    Write-Host "[2/4] Installationspaket packen"
    Invoke-Dotnet msbuild (Join-Path $PSScriptRoot "Zip.proj") "-p:SourceDirectory=$appDir" "-p:DestinationFile=$payload" -nologo -v:quiet

    Write-Host "[3/4] Setup als Single-File veröffentlichen"
    Invoke-Dotnet publish (Join-Path $root "StudyScope.Setup\StudyScope.Setup.csproj") -c $Configuration -r $rid --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none "-p:PayloadZip=$payload" -o $setupDir --nologo -v quiet

    Write-Host "[4/4] Auslieferung erstellen"
    if ($rid -like "win-*") {
        $output = Join-Path $artifacts "StudyScope-$version-Setup.exe"
        Copy-Item (Join-Path $setupDir "StudyScope.Setup.exe") $output -Force
    }
    else {
        $name = "StudyScope-$version-$rid"
        $stage = Join-Path $artifacts "stage\$name"
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        New-Item -ItemType Directory -Force $stage | Out-Null
        Copy-Item (Join-Path $setupDir "StudyScope.Setup") $stage
        Copy-Item (Join-Path $root "StudyScope.Setup\Packaging\linux\install.sh") $stage

        $output = Join-Path $artifacts "$name.tar.gz"
        # tar.exe (bsdtar, ab Windows 10 enthalten) – Ausführungsrechte setzt install.sh selbst
        & "$env:SystemRoot\System32\tar.exe" -czf $output -C (Split-Path $stage) $name
        if ($LASTEXITCODE -ne 0) { throw "tar ist fehlgeschlagen." }
        Remove-Item (Split-Path $stage) -Recurse -Force
    }

    $file = Get-Item $output
    $hash = (Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ("Fertig: {0}  ({1:N1} MB)" -f $file.FullName, ($file.Length / 1MB)) -ForegroundColor Green
    Write-Host "SHA256: $hash"
}
