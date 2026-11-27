using System.Net;
using Microsoft.AspNetCore.Components;
using StudyScope.Core.Localization;

namespace StudyScope.Setup.Resources;

/// <summary>
/// Texte mit einfachem HTML (<c>&lt;strong&gt;</c>, <c>&lt;code&gt;</c>) aus den Ressourcen ausgeben – wie
/// <c>StudyScope.UI.Resources.LocHtml</c>. Die eingesetzten Werte (Pfade, Versionen) werden HTML-maskiert.
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
