using System.Globalization;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;

namespace StudyScope.UI.Services;

/// <summary>
/// Sprache der Oberfläche: Sprachen aus den Satellitenordnern neben der exe (<see cref="UiLanguage.AvailableCultures"/>),
/// Vorrang für <c>STUDYSCOPE_UI_CULTURE</c>, sonst Einstellung → Systemsprache → Englisch → Deutsch.
/// </summary>
public sealed class UiLanguageService : IUiLanguageService
{
    private readonly Lazy<IReadOnlyList<string>> _available = new(() =>
        UiLanguage.AvailableCultures(AppContext.BaseDirectory, typeof(UiLanguageService).Assembly.GetName().Name!));

    public IReadOnlyList<string> AvailableCultures => _available.Value;

    public string? OverrideCulture
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(UiLanguage.OverrideVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public CultureInfo Resolve(string? setting) =>
        UiLanguage.Resolve(OverrideCulture ?? setting, CultureInfo.InstalledUICulture, AvailableCultures);

    /// <summary>
    /// Vorgabe für alle Abläufe und die Kultur des laufenden Ereignisses; die App setzt beim Start nur die Vorgabe
    /// (siehe <see cref="UiLanguage.ApplyDefaults"/>), daher wirkt der Wechsel überall.
    /// </summary>
    public void Apply(CultureInfo culture) => UiLanguage.Apply(culture);
}
