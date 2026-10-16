using System.Diagnostics;
using System.Runtime.InteropServices;
using StudyScope.Core.Interfaces;

namespace StudyScope.Infrastructure.Services;

public class FileOpenerService : IFileOpenerService
{
    public bool OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", $"\"{filePath}\"");
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"\"{filePath}\"");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Öffnen: {ex.Message}");
            return false;
        }
    }
}