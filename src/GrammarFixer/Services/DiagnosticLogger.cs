using System.Runtime.CompilerServices;
using System.IO;

namespace GrammarFixer.Services;

public enum DiagnosticLogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public static class DiagnosticLogger
{
    private static readonly object Sync = new();
    public static readonly string LogDirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GrammarFixer", "logs");

    static DiagnosticLogger()
    {
        try
        {
            Directory.CreateDirectory(LogDirectoryPath);
            var cutoff = DateTime.UtcNow.Date.AddDays(-7);
            foreach (var file in Directory.GetFiles(LogDirectoryPath, "grammerfixer_*.log"))
            {
                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(file);
                    if (lastWrite < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // never throw from logger
                }
            }
        }
        catch
        {
            // never throw from logger
        }
    }

    public static void Log(DiagnosticLogLevel level, string message, [CallerMemberName] string caller = "")
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectoryPath);
                var filePath = Path.Combine(LogDirectoryPath, $"grammerfixer_{DateTime.Now:yyyy-MM-dd}.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] [{caller}] {message}{Environment.NewLine}";
                File.AppendAllText(filePath, line);
            }
        }
        catch
        {
            // never throw from logger
        }
    }
}
