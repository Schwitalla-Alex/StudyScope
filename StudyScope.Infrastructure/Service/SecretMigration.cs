using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyScope.Core.Interfaces;

namespace StudyScope.Infrastructure.Services;

/// <summary>Ergebnis der Migration beim Start.</summary>
public sealed record SecretMigrationResult(int MigratedKeys, string? BackupPath, IReadOnlyList<string> DeletedBackups, IReadOnlyList<string> Errors);

/// <summary>
/// Schützt beim Start alle gespeicherten API-Schlüssel neu, die noch in einem älteren Format stehen (1.2.0, Klartext,
/// mehrfach verschlüsselt), in <c>config.json</c> und allen Profilen – sonst blieben sie in selten gespeicherten
/// Profilen lange schwach geschützt.
/// <list type="bullet">
/// <item>Vor der ersten Änderung entsteht eine Sicherung <c>Backup/keys-1.2-&lt;Datum&gt;.zip</c> der betroffenen Dateien
/// (darin stehen die alten Werte, genauso geschützt wie vorher). Ohne Sicherung wird nichts geändert.</item>
/// <item>Ein Wert wird nur ersetzt, wenn der neu geschützte Wert genau den alten Schlüssel ergibt; Dateien werden über
/// <c>.tmp</c> + Verschieben geschrieben. Nicht lesbare Werte bleiben unverändert.</item>
/// <item>Sicherungen werden nach <see cref="BackupRetention"/> (30 Tage) beim Start gelöscht.</item>
/// </list>
/// Fehler brechen den Start nie ab, sie stehen im Ergebnis.
/// </summary>
public static class SecretMigration
{
    public const string BackupFolderName = "Backup";
    public const string BackupFilePrefix = "keys-1.2-";
    private const string BackupDateFormat = "yyyyMMdd-HHmmss";

    /// <summary>So lange bleibt eine Sicherung erhalten.</summary>
    public static readonly TimeSpan BackupRetention = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static readonly string[][] ConfigKeyPaths =
    {
        JsonStorageService.ConfigAiKeyPath, JsonStorageService.ConfigGeminiKeyPath, JsonStorageService.ConfigTavilyKeyPath
    };

    private static readonly string[][] ProfileKeyPaths =
    {
        JsonStorageService.ProfileAiKeyPath, JsonStorageService.ProfileGeminiKeyPath
    };

    public static SecretMigrationResult Run(string baseDirectory, ISecretProtector protector, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        var errors = new List<string>();
        var deleted = DeleteExpiredBackups(baseDirectory, time.GetLocalNow(), errors);

        // 1. Alle Dateien prüfen und die neuen Inhalte im Speicher vorbereiten
        var pending = new List<(string Path, JsonObject Root, int Keys)>();

        foreach (var (path, keyPaths) in CandidateFiles(baseDirectory))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
                    continue;

                var changed = 0;
                foreach (var keyPath in keyPaths)
                {
                    if (TryUpgrade(root, keyPath, protector))
                        changed++;
                }

                if (changed > 0)
                {
                    pending.Add((path, root, changed));

                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (pending.Count == 0)
            return new SecretMigrationResult(0, null, deleted, errors);

        // 2. Sicherung vor der ersten Änderung; ohne Sicherung bleibt alles, wie es ist
        string backupPath;
        try
        {
            backupPath = CreateBackup(baseDirectory, pending.Select(p => p.Path), time.GetLocalNow());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(ex.Message);
            return new SecretMigrationResult(0, null, deleted, errors);
        }

        // 3. Je Datei über .tmp schreiben und verschieben
        var migrated = 0;
        foreach (var (path, root, keys) in pending)
        {
            var tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, root.ToJsonString(WriteOptions));
                File.Move(tmp, path, overwrite: true);
                migrated += keys;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                TryDelete(tmp);
            }
        }

        return new SecretMigrationResult(migrated, backupPath, deleted, errors);
    }

    /// <summary>Ersetzt einen Wert im alten Format durch den neu geschützten, wenn dieser genau den alten Schlüssel ergibt.</summary>
    private static bool TryUpgrade(JsonObject root, IReadOnlyList<string> keyPath, ISecretProtector protector)
    {
        var stored = JsonStorageService.StoredSecret(root, keyPath);
        var read = protector.Unprotect(stored);
        if (read.Status != SecretReadStatus.NeedsUpgrade)
            return false;

        var protectedValue = protector.Protect(read.Value);
        var check = protector.Unprotect(protectedValue);
        if (check.Status != SecretReadStatus.Ok || check.Value != read.Value)
            return false;

        var (parent, name) = JsonStorageService.FindProperty(root, keyPath);
        if (parent == null || name == null)
            return false;

        parent[name] = protectedValue;
        return true;
    }

    private static IEnumerable<(string Path, string[][] KeyPaths)> CandidateFiles(string baseDirectory)
    {
        var config = Path.Combine(baseDirectory, "config.json");
        if (File.Exists(config))
            yield return (config, ConfigKeyPaths);

        var profiles = Path.Combine(baseDirectory, "Profiles");
        if (!Directory.Exists(profiles))
            yield break;

        foreach (var file in Directory.GetFiles(profiles, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            yield return (file, ProfileKeyPaths);
    }

    private static string CreateBackup(string baseDirectory, IEnumerable<string> files, DateTimeOffset now)
    {
        var folder = Path.Combine(baseDirectory, BackupFolderName);
        Directory.CreateDirectory(folder);

        var name = BackupFilePrefix + now.ToString(BackupDateFormat, CultureInfo.InvariantCulture);
        var path = Path.Combine(folder, name + ".zip");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name}-{i}.zip");

        var tmp = path + ".part";
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    var entry = Path.GetRelativePath(baseDirectory, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, entry, CompressionLevel.Optimal);
                }
            }
            File.Move(tmp, path);
        }
        finally
        {
            TryDelete(tmp);
        }
        return path;
    }

    /// <summary>Löscht Sicherungen, die älter als <see cref="BackupRetention"/> sind (Datum aus dem Dateinamen, sonst Änderungszeit).</summary>
    public static IReadOnlyList<string> DeleteExpiredBackups(string baseDirectory, DateTimeOffset now, List<string>? errors = null)
    {
        var deleted = new List<string>();
        var folder = Path.Combine(baseDirectory, BackupFolderName);
        if (!Directory.Exists(folder))
            return deleted;

        foreach (var file in Directory.GetFiles(folder, BackupFilePrefix + "*.zip"))
        {
            var created = BackupDate(file) ?? new DateTimeOffset(File.GetLastWriteTime(file));
            if (now - created < BackupRetention)
                continue;

            try
            {
                File.Delete(file);
                deleted.Add(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors?.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return deleted;
    }

    private static DateTimeOffset? BackupDate(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (name.Length < BackupFilePrefix.Length + BackupDateFormat.Length)
            return null;

        var text = name.Substring(BackupFilePrefix.Length, BackupDateFormat.Length);
        return DateTime.TryParseExact(text, BackupDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
            ? new DateTimeOffset(date)
            : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Rest im Ordner stört nicht
        }
    }
}
