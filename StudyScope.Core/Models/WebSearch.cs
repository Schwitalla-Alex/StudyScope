using System.Text.Json.Serialization;
using StudyScope.Core.Common;

namespace StudyScope.Core.Models;

/// <summary>Suchdienst der Websuche im Chat. Gespeichert als Text („Tavily“, „Searxng“).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WebSearchProviderType>))]
public enum WebSearchProviderType
{
    /// <summary>Kein Suchdienst eingerichtet.</summary>
    None,
    /// <summary>Tavily (Cloud-Dienst mit eigenem API-Schlüssel, Gratis-Kontingent).</summary>
    Tavily,
    /// <summary>SearXNG (eigene bzw. selbst gewählte Instanz, ohne Schlüssel).</summary>
    Searxng
}

/// <summary>
/// Globale Einstellungen der Websuche (gehören zum Rechner bzw. Konto, nicht zum Profil) als Eigenschaft
/// <c>WebSearchSettings</c> in <c>config.json</c>. Ob ein Profil im Internet sucht, steht dagegen im Profil
/// (<see cref="WebSearchProfileState"/>, standardmäßig aus).
/// </summary>
public class WebSearchSettings
{
    public const int DefaultMaxResults = 5;
    public const int MinMaxResults = 1;
    public const int MaxMaxResults = 10;

    public WebSearchProviderType Provider { get; set; } = WebSearchProviderType.None;

    /// <summary>Tavily-Schlüssel; im Speicher Klartext, in <c>config.json</c> verschlüsselt.</summary>
    public string TavilyApiKey { get; set; } = string.Empty;

    /// <summary>Basisadresse der SearXNG-Instanz (http/https), z. B. „http://localhost:8888“.</summary>
    public string SearxngUrl { get; set; } = string.Empty;

    /// <summary>Anzahl der Treffer je Frage (1–10, Standard 5).</summary>
    public int MaxResults { get; set; } = DefaultMaxResults;

    /// <summary>Ein Suchdienst ist gewählt und die nötige Angabe (Schlüssel bzw. Adresse) ist vorhanden.</summary>
    [JsonIgnore]
    public bool IsConfigured => Provider switch
    {
        WebSearchProviderType.Tavily => !string.IsNullOrWhiteSpace(TavilyApiKey),
        WebSearchProviderType.Searxng => IsValidSearxngUrl(SearxngUrl),
        _ => false
    };

    /// <summary>Anzeigename des gewählten Dienstes (Eigenname, nicht übersetzt); leer ohne Dienst.</summary>
    [JsonIgnore]
    public string ProviderName => NameOf(Provider);

    public static string NameOf(WebSearchProviderType provider) => provider switch
    {
        WebSearchProviderType.Tavily => "Tavily",
        WebSearchProviderType.Searxng => "SearXNG",
        _ => string.Empty
    };

    /// <summary>Begrenzt die Trefferzahl auf 1–10; 0 bzw. ein fehlender Wert ergibt den Standard.</summary>
    public static int ClampMaxResults(int value) =>
        value <= 0 ? DefaultMaxResults : Math.Clamp(value, MinMaxResults, MaxMaxResults);

    /// <summary>SearXNG-Adresse: absolute http/https-Adresse mit Host, ohne Benutzerdaten.</summary>
    public static bool IsValidSearxngUrl(string? url) => ExternalUrl.TryParse(url, out _);

    public WebSearchSettings Clone() => new()
    {
        Provider = Provider,
        TavilyApiKey = TavilyApiKey,
        SearxngUrl = SearxngUrl,
        MaxResults = MaxResults
    };
}

/// <summary>
/// Websuche eines Profils: eingeschaltet (Standard aus) und für welchen Suchdienst der Hinweis bestätigt wurde
/// (Name des Dienstes; null = noch nie). Wechselt der Dienst, erscheint der Hinweis erneut.
/// </summary>
public sealed record WebSearchProfileState(bool Enabled = false, string? NoticeAcceptedFor = null)
{
    public static WebSearchProfileState Off { get; } = new();

    /// <summary>Vor dem Einschalten ist der Hinweis für diesen Dienst (noch) zu zeigen.</summary>
    public bool NeedsNotice(WebSearchProviderType provider) => NeedsNotice(WebSearchSettings.NameOf(provider));

    /// <summary>Vor dem Einschalten ist der Hinweis für den Dienst mit diesem Namen (noch) zu zeigen.</summary>
    public bool NeedsNotice(string providerName) =>
        string.IsNullOrEmpty(providerName) || !string.Equals(NoticeAcceptedFor, providerName, StringComparison.Ordinal);

    /// <summary>
    /// Die Websuche ist für dieses Profil wirklich an: eingeschaltet und der Hinweis für den jetzt eingerichteten Dienst
    /// bestätigt. Nach einem Wechsel des Dienstes ist sie aus, bis der Hinweis erneut bestätigt wurde.
    /// </summary>
    public bool IsActiveFor(string providerName) => Enabled && !NeedsNotice(providerName);
}

/// <summary>Ein Treffer der Websuche. Wird nicht gespeichert – nur für die eine Anfrage im Speicher.</summary>
public sealed record WebSearchResult(string Title, string Url, string Snippet, DateTimeOffset? PublishedAt = null)
{
    /// <summary>Host ohne „www.“, z. B. „example.org“ (für die Quellenanzeige).</summary>
    public string Domain => Uri.TryCreate(Url, UriKind.Absolute, out var uri)
        ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
        : string.Empty;
}

/// <summary>
/// Suchanfrage, die ins Internet geht. Grundsatz: Ins Netz geht nur Text, den der Benutzer selbst eingegeben hat –
/// nie Auszüge, Dateinamen, Profilnamen, Knotentitel oder andere von App bzw. KI erzeugte Texte. Deshalb gibt es nur
/// zwei Wege zu einer Anfrage: <see cref="FromUserQuestion"/> (die selbst eingegebene Frage) und
/// <see cref="ConnectionTest"/> (fester Begriff für „Verbindung testen“).
/// </summary>
public sealed class WebSearchQuery
{
    /// <summary>Höchstlänge der Anfrage in Zeichen.</summary>
    public const int MaxLength = 400;

    /// <summary>Suchbegriff für „Verbindung testen“ (fest, enthält keine Benutzerdaten).</summary>
    public const string ConnectionTestText = "StudyScope";

    private WebSearchQuery(string text) => Text = text;

    /// <summary>Der Text, der an den Suchdienst geht.</summary>
    public string Text { get; }

    /// <summary>
    /// Anfrage aus der Frage, die der Benutzer selbst eingegeben hat (vor dem Anhängen der Sprachzeile).
    /// Zeilenumbrüche und Steuerzeichen werden zu Leerzeichen, mehrfache Leerzeichen zusammengefasst, die Länge auf
    /// <see cref="MaxLength"/> Zeichen an einer Wortgrenze gekürzt.
    /// </summary>
    /// <exception cref="ArgumentException">Die Frage ist leer.</exception>
    public static WebSearchQuery FromUserQuestion(string userQuestion)
    {
        var text = Normalize(userQuestion);
        if (text.Length == 0)
            throw new ArgumentException("Die Frage ist leer.", nameof(userQuestion));
        return new WebSearchQuery(Shorten(text));
    }

    /// <summary>Anfrage für „Verbindung testen“.</summary>
    public static WebSearchQuery ConnectionTest() => new(ConnectionTestText);

    public override string ToString() => Text;

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new System.Text.StringBuilder(value.Length);
        var lastWasSpace = true;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '﻿')
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                builder.Append(c);
                lastWasSpace = false;
            }
        }
        return builder.ToString().Trim();
    }

    private static string Shorten(string text)
    {
        if (text.Length <= MaxLength) return text;
        var cut = text.LastIndexOf(' ', MaxLength);
        var end = cut >= MaxLength / 2 ? cut : MaxLength;
        // Kein halbes Ersatzzeichenpaar (Emoji) am Ende
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end].TrimEnd();
    }
}
