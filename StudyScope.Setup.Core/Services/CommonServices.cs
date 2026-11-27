using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using StudyScope.Core.Common;
using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Resources;

namespace StudyScope.Setup.Core.Services;

/// <summary>
/// Installationspaket als eingebettete Ressource "payload.zip" (vom Build-Skript per -p:PayloadZip=... eingebunden).
/// Setup und App haben über Directory.Build.props dieselbe Version.
/// </summary>
public sealed class EmbeddedPayloadProvider : IPayloadProvider
{
    public const string ResourceName = "payload.zip";

    private readonly Assembly _assembly;

    public EmbeddedPayloadProvider(Assembly assembly) => _assembly = assembly;

    public bool IsAvailable => _assembly.GetManifestResourceInfo(ResourceName) != null;

    public string Version => AppInfo.Version;

    public Stream OpenPayload() =>
        _assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException(SetupCoreStrings.Install_NoPayload);
}

/// <summary>Prozesse prüfen und starten (auf beiden Plattformen gleich).</summary>
public sealed class ProcessService : IProcessService
{
    public bool IsAppRunning(string installDirectory, string appExecutableName)
    {
        var expected = Path.Combine(installDirectory, appExecutableName);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(appExecutableName)))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path == null || string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected),
                            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        return true;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Pfad nicht lesbar → vorsichtshalber als laufend werten
                    return true;
                }
            }
        }
        return false;
    }

    public void StartApp(string executablePath, string workingDirectory)
    {
        Process.Start(new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        })?.Dispose();
    }
}

/// <summary>
/// Gibt Fortschritt direkt (ohne SynchronizationContext) weiter. Die Oberfläche aktualisiert sich über
/// PropertyChanged + InvokeAsync(StateHasChanged) selbst im richtigen Thread.
/// </summary>
public sealed class DirectProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public DirectProgress(Action<T> handler) => _handler = handler;
    public void Report(T value) => _handler(value);
}

/// <summary>
/// Erzeugt den Inhalt der .desktop-Datei nach der freedesktop.org Desktop Entry Specification.
/// Plattformneutral, damit er auch unter Windows getestet werden kann. Beschreibung, Suchbegriffe und die Aktion
/// „Deinstallieren“ stehen in der Sprache des Setups (wie Startmenü-Verknüpfungen unter Windows).
/// </summary>
public static class DesktopEntryBuilder
{
    public const string FileName = "studyscope.desktop";
    public const string IconName = "studyscope";

    public static string Build(string installDirectory, string appExecutablePath, string? setupExecutablePath, string icon)
    {
        var sb = new StringBuilder();
        sb.Append("[Desktop Entry]\n");
        sb.Append("Type=Application\n");
        sb.Append("Version=1.5\n");
        sb.Append("Name=StudyScope\n");
        sb.Append("GenericName=").Append(EscapeValue(SetupCoreStrings.Desktop_GenericName)).Append('\n');
        sb.Append("Comment=").Append(EscapeValue(SetupCoreStrings.Desktop_Comment)).Append('\n');
        sb.Append("Exec=").Append(QuoteExecArgument(appExecutablePath)).Append('\n');
        sb.Append("Path=").Append(EscapeValue(installDirectory)).Append('\n');
        sb.Append("Icon=").Append(EscapeValue(icon)).Append('\n');
        sb.Append("Terminal=false\n");
        sb.Append("Categories=Education;Office;\n");
        sb.Append("Keywords=").Append(EscapeValue(SetupCoreStrings.Desktop_Keywords)).Append('\n');
        sb.Append("StartupWMClass=StudyScope.UI\n");

        if (!string.IsNullOrEmpty(setupExecutablePath))
        {
            sb.Append("Actions=Uninstall;\n");
            sb.Append('\n');
            sb.Append("[Desktop Action Uninstall]\n");
            sb.Append("Name=").Append(EscapeValue(SetupCoreStrings.Desktop_UninstallAction)).Append('\n');
            sb.Append("Exec=").Append(QuoteExecArgument(setupExecutablePath)).Append(" --uninstall\n");
        }

        return sb.ToString();
    }

    /// <summary>Liest den Installationsordner (Path=) aus einer vorhandenen .desktop-Datei.</summary>
    public static string? ReadPath(string desktopFileContent)
    {
        foreach (var line in desktopFileContent.Split('\n'))
        {
            if (line.StartsWith("Path=", StringComparison.Ordinal))
                return UnescapeValue(line["Path=".Length..].TrimEnd('\r'));
        }
        return null;
    }

    /// <summary>Exec-Argument: in doppelte Anführungszeichen, ", `, $ und \ maskiert, % verdoppelt.</summary>
    public static string QuoteExecArgument(string argument)
    {
        var escaped = new StringBuilder();
        foreach (var c in argument)
        {
            if (c is '"' or '`' or '$' or '\\')
                escaped.Append('\\');
            escaped.Append(c);
        }
        // Erst Exec-Quoting, dann allgemeines Escaping des Werts ("\" → "\\"), dann Feldcodes schützen
        return "\"" + EscapeValue(escaped.ToString()).Replace("%", "%%") + "\"";
    }

    private static string EscapeValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r");

    private static string UnescapeValue(string value)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                sb.Append(value[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 's' => ' ', _ => value[i] });
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }
}
