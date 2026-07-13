using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GrammarFixer.Services;

namespace GrammarFixer.Services;

/// <summary>
/// Manages the LanguageTool local server process (java -jar languagetool-server.jar).
/// </summary>
public sealed class LanguageToolService : IDisposable
{
    public const int DefaultPort = 8081;
    public static string BaseUrl => $"http://localhost:{DefaultPort}";

    private Process? _serverProcess;
    private bool _ready;
    private bool _disposed;

    public bool IsReady => _ready;
    public int Port { get; } = DefaultPort;

    /// <summary>
    /// Starts the LanguageTool server.
    /// Waits up to 30 seconds for /v2/languages to respond.
    /// Returns true if server started successfully, false if Java/JAR not found.
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        if (_ready) return true;

        var jarPath = FindJarPath();
        if (jarPath == null)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Warn, "LanguageTool JAR not found in tools/ folder");
            return false;
        }

        if (!IsJavaAvailable())
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Warn, "Java not found in PATH");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "java",
            Arguments = $"-Dfile.encoding=utf-8 -Xmx512m -jar \"{jarPath}\" --port {Port} --allow-origin \"*\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(jarPath)
        };

        try
        {
            _serverProcess = Process.Start(psi)!;
            _serverProcess.EnableRaisingEvents = true;
            _serverProcess.Exited += (_, _) => _ready = false;
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"LanguageTool process started PID={_serverProcess.Id}");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"Failed to start LanguageTool: {ex.Message}");
            return false;
        }

        // Read stderr/stdout in background to prevent pipe deadlock
        _ = Task.Run(() => ReadOutputAsync(_serverProcess.StandardOutput, "stdout"));
        _ = Task.Run(() => ReadOutputAsync(_serverProcess.StandardError, "stderr"));

        // Poll health endpoint (max 30s)
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var resp = await http.GetAsync($"{BaseUrl}/v2/languages", ct);
                if (resp.IsSuccessStatusCode)
                {
                    _ready = true;
                    DiagnosticLogger.Log(DiagnosticLogLevel.Info, "LanguageTool server ready");
                    return true;
                }
            }
            catch { /* ignore */ }
            
            await Task.Delay(500, ct);
        }

        DiagnosticLogger.Log(DiagnosticLogLevel.Warn, "LanguageTool server did not become ready in time");
        return false;
    }

    private string? FindJarPath()
    {
        // Check multiple locations for the JAR
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            System.IO.Path.Combine(baseDir, "tools", "languagetool-server.jar"),
            System.IO.Path.Combine(baseDir, "..", "..", "tools", "languagetool-server.jar"),
            System.IO.Path.Combine(Environment.CurrentDirectory, "tools", "languagetool-server.jar")
        };

        foreach (var p in candidates)
        {
            var full = System.IO.Path.GetFullPath(p);
            if (System.IO.File.Exists(full))
                return full;
        }
        return null;
    }

    private static bool IsJavaAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "java",
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            p?.WaitForExit(2000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task ReadOutputAsync(System.IO.StreamReader reader, string label)
    {
        try
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                    DiagnosticLogger.Log(DiagnosticLogLevel.Debug, $"LT[{label}]: {line}");
            }
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_serverProcess is { HasExited: false })
            {
                _serverProcess.Kill(true); // .NET 7+ entire process tree
                _serverProcess.WaitForExit(2000);
            }
        }
        catch { }
        finally
        {
            _serverProcess?.Dispose();
            _serverProcess = null;
            _ready = false;
        }
    }
}