# Schreibt eine lesbare Zusammenfassung der Testergebnisse (trx) in die Job-Zusammenfassung von GitHub Actions.
# Aufruf: pwsh .github/scripts/test-summary.ps1 -ResultsDirectory TestResults -Title "Tests (Windows)"
# Bricht nie ab: Ob der Job rot wird, entscheidet der Testschritt selbst.
param(
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [string]$Title = "Testergebnisse"
)

$ErrorActionPreference = "Continue"
$summary = $env:GITHUB_STEP_SUMMARY
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("## $Title")
$lines.Add("")

$files = @(Get-ChildItem -Path $ResultsDirectory -Filter *.trx -Recurse -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) {
    $lines.Add(":warning: Keine Testergebnisse (trx) gefunden – der Build ist vermutlich vorher fehlgeschlagen.")
}
else {
    $total = 0; $passed = 0; $failed = 0; $skipped = 0
    $failedTests = [System.Collections.Generic.List[string]]::new()
    $skippedTests = [System.Collections.Generic.List[string]]::new()
    $duration = [TimeSpan]::Zero

    foreach ($file in $files) {
        [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
        $ns = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
        $ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

        $counters = $trx.SelectSingleNode("//t:ResultSummary/t:Counters", $ns)
        if ($counters) {
            $total += [int]$counters.total
            $passed += [int]$counters.passed
            $failed += [int]$counters.failed + [int]$counters.error + [int]$counters.timeout + [int]$counters.aborted
            $skipped += [int]$counters.notExecuted
        }
        $times = $trx.SelectSingleNode("//t:Times", $ns)
        if ($times -and $times.start -and $times.finish) {
            $duration += ([DateTimeOffset]$times.finish - [DateTimeOffset]$times.start)
        }

        foreach ($result in $trx.SelectNodes("//t:UnitTestResult", $ns)) {
            $message = $result.SelectSingleNode("t:Output/t:ErrorInfo/t:Message", $ns)
            $text = if ($message) { ($message.InnerText -replace '\s+', ' ').Trim() } else { "" }
            if ($text.Length -gt 300) { $text = $text.Substring(0, 300) + " …" }
            $text = $text -replace '\|', '\|'
            switch ($result.outcome) {
                "Failed"      { $failedTests.Add("| ``$($result.testName)`` | $text |") }
                "NotExecuted" { $skippedTests.Add("| ``$($result.testName)`` | $text |") }
            }
        }
    }

    # Der NUnit-Adapter zählt übersprungene Tests nicht unter „notExecuted“ – daher aus der Differenz
    $skipped = [Math]::Max($skipped, $total - $passed - $failed)
    $icon = if ($failed -gt 0) { ":x:" } else { ":white_check_mark:" }
    $lines.Add("$icon **$passed von $total bestanden**, $failed fehlgeschlagen, $skipped übersprungen · Dauer $([int]$duration.TotalSeconds) s")
    $lines.Add("")
    if ($failedTests.Count -gt 0) {
        $lines.Add("### Fehlgeschlagen")
        $lines.Add("| Test | Meldung |")
        $lines.Add("|---|---|")
        $failedTests | ForEach-Object { $lines.Add($_) }
        $lines.Add("")
    }
    if ($skippedTests.Count -gt 0) {
        $lines.Add("<details><summary>Übersprungen ($($skippedTests.Count)) – meist Tests für das andere Betriebssystem</summary>")
        $lines.Add("")
        $lines.Add("| Test | Grund |")
        $lines.Add("|---|---|")
        $skippedTests | ForEach-Object { $lines.Add($_) }
        $lines.Add("")
        $lines.Add("</details>")
    }
}

$text = ($lines -join "`n") + "`n"
if ($summary) { Add-Content -LiteralPath $summary -Value $text -Encoding UTF8 } else { Write-Output $text }
exit 0
