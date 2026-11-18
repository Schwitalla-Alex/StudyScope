namespace StudyScope.Tests.Common;

/// <summary>
/// Tests, die ein Werkzeug auf dem Rechner brauchen (ffmpeg, native SkiaSharp-Bibliothek): lokal ohne das Werkzeug
/// übersprungen (Ignore mit Grund). In der CI ist <c>STUDYSCOPE_REQUIRE_INTEGRATION=1</c> gesetzt – dort schlägt
/// der Test stattdessen fehl, damit ein fehlendes Werkzeug nicht unbemerkt Tests überspringt.
/// </summary>
internal static class IntegrationRequirement
{
    public const string EnvironmentVariable = "STUDYSCOPE_REQUIRE_INTEGRATION";

    public static bool IsRequired => Environment.GetEnvironmentVariable(EnvironmentVariable) == "1";

    /// <summary>Überspringt den Test (lokal) bzw. lässt ihn fehlschlagen (CI).</summary>
    public static void Unavailable(string reason)
    {
        if (IsRequired) Assert.Fail($"{reason} – in der CI ist dieser Test Pflicht ({EnvironmentVariable}=1).");
        Assert.Ignore(reason);
    }
}
