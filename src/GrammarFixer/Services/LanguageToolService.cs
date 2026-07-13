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
    // Use 127.0.0.1 — LT binds IPv4 only; "localhost" can add ~2s on first .NET HttpClient request.
    public static string BaseUrl => $"http://127.0.0.1:{DefaultPort}";

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

        if (await ProbeServerAsync(ct))
        {
            _ready = true;
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, "Reusing existing LanguageTool server");
            return true;
        }

        var jarPath = FindJarPath();
        if (jarPath == null)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Warn, "LanguageTool JAR not found in tools/ folder");
            return false;
        }

        var toolsDir = System.IO.Path.GetDirectoryName(jarPath)!;
        var libsDir = System.IO.Path.Combine(toolsDir, "libs");
        var libsJarCount = System.IO.Directory.Exists(libsDir)
            ? System.IO.Directory.GetFiles(libsDir, "*.jar").Length
            : 0;

        if (!IsJavaAvailable())
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Warn, "Java not found in PATH");
            return false;
        }

        if (!System.IO.Directory.Exists(libsDir) || libsJarCount == 0)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Warn,
                "LanguageTool libs/ folder missing next to languagetool-server.jar. Copy the full LanguageTool ZIP contents into tools/.");
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
            WorkingDirectory = toolsDir
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

        return await WaitForServerReadyAsync(ct);
    }

    private static async Task<bool> ProbeServerAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await http.GetAsync($"{BaseUrl}/v2/languages", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<bool> WaitForServerReadyAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (await ProbeServerAsync(ct))
            {
                _ready = true;
                DiagnosticLogger.Log(DiagnosticLogLevel.Info, "LanguageTool server ready");
                return true;
            }

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
