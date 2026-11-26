using System.Net;
using Microsoft.AspNetCore.Components;
using StudyScope.Core.Localization;

namespace StudyScope.UI.Resources;

/// <summary>
/// Texte mit einfachem HTML (z. B. <c>&lt;b&gt;</c>) aus den Ressourcen ausgeben. Die eingesetzten Werte werden
/// HTML-maskiert, das Markup gehört allein dem übersetzten Text – so bleibt die Wortstellung je Sprache frei.
/// </summary>
public static class LocHtml
{
    public static MarkupString Format(string pattern, params (string Name, object? Value)[] args)
    {
        var encoded = args
            .Select(a => (a.Name, a.Value is string s ? (object?)WebUtility.HtmlEncode(s) : a.Value))
            .ToArray();
        return new MarkupString(Loc.Format(pattern, encoded));
    }
}
