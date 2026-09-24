using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IfredrixDownloadManager;

/// <summary>
/// Runs Tor on demand using the user's own <c>tor.exe</c> — preferably the
/// one shipped inside Tor Browser, so nothing extra must be downloaded.
/// The process gets its own data directory plus the bundle's geoip files,
/// serves SOCKS on the configured endpoint, and is stopped with the app.
/// </summary>
public sealed class TorManager : IDisposable
{
    private static readonly Regex BootstrapRegex =
        new(@"Bootstrapped\s+(\d+)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private Process? _process;
    private bool _disposed;

    public bool IsRunning => _process is { HasExited: false };
    public int BootstrapPercent { get; private set; }
    public bool IsReady => IsRunning && BootstrapPercent >= 100;

    public event EventHandler<string>? StatusChanged;

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ifredrixDownloadManager", "tor-data");

    /// <summary>Locates tor.exe: explicit setting, Tor Browser spots, then PATH.</summary>
    public static string? FindTorExe(string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
        {
            return preferred;
        }

        // Our own one-click bundle from Tor setup.
        try
        {
            if (File.Exists(TorBundle.BundledExePath) &&
                new FileInfo(TorBundle.BundledExePath).Length > 1024 * 1024)
            {
                return TorBundle.BundledExePath;
            }
        }
        catch
        {
            // Fall through to the other spots.
        }

        var candidates = new List<string>();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        foreach (var root in new[] { desktop, programFiles, programFilesX86, localAppData })
        {
            if (string.IsNullOrEmpty(root)) continue;
            candidates.Add(Path.Combine(root, "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"));
        }

        foreach (var exe in candidates)
        {
            try { if (File.Exists(exe)) return exe; } catch { }
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "tor.exe",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p != null && p.WaitForExit(3000) && p.ExitCode == 0)
            {
                var first = p.StandardOutput.ReadToEnd().Split('\n')
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.EndsWith("tor.exe", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(first) && File.Exists(first)) return first;
            }
        }
        catch
        {
            // No tor on PATH.
        }

        return null;
    }

    private static string? GeoFile(string torExe, string name)
    {
        // Tor Browser layout: <root>\Browser\TorBrowser\{Tor\tor.exe, Data\Tor\geoip*}.
        try
        {
            var torDir = Path.GetDirectoryName(torExe);
            var bundle = Directory.GetParent(torDir ?? string.Empty)?.Parent?.FullName;
            if (bundle == null) return null;
            var full = Path.Combine(bundle, "Data", "Tor", name);
            if (File.Exists(full)) return full;
        }
        catch
        {
            // Best effort only.
        }
        // One-click bundle layout: tor.exe flat beside data\, or nested
        // as tor/tor.exe beside data\. Either way the geoip files (if the
        // bundle ships them) sit next to the exe tree, not in Tor Browser's
        // Data\Tor spot checked above.
        try
        {
            var torDir = Path.GetDirectoryName(torExe);
            foreach (var baseDir in new[]
                { torDir, Directory.GetParent(torDir ?? string.Empty)?.FullName })
            {
                if (string.IsNullOrEmpty(baseDir)) continue;
                var full = Path.Combine(baseDir, "data", name);
                if (File.Exists(full)) return full;
            }
        }
        catch
        {
            // Best effort only.
        }
        return null;
    }

    public async Task StartAsync(
        string torExe, string socksHost, int socksPort,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        Stop();
        BootstrapPercent = 0;

        Directory.CreateDirectory(DataDirectory);

        var args = $"--SocksPort {socksPort} --DataDirectory \"{DataDirectory}\"";
        var geoip = GeoFile(torExe, "geoip");
        var geoip6 = GeoFile(torExe, "geoip6");
        if (geoip != null) args += $" --GeoIPFile \"{geoip}\"";
        if (geoip6 != null) args += $" --GeoIPv6File \"{geoip6}\"";

        var psi = new ProcessStartInfo
        {
            FileName = torExe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(torExe) ?? string.Empty
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var m = BootstrapRegex.Match(e.Data);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var pct))
            {
                BootstrapPercent = Math.Max(BootstrapPercent, pct);
                progress?.Report(BootstrapPercent);
                StatusChanged?.Invoke(this, $"starting {BootstrapPercent}%");
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var m = BootstrapRegex.Match(e.Data);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var pct))
            {
                BootstrapPercent = Math.Max(BootstrapPercent, pct);
                progress?.Report(BootstrapPercent);
                StatusChanged?.Invoke(this, $"starting {BootstrapPercent}%");
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start tor.exe.");
        }

        _process = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        StatusChanged?.Invoke(this, "starting 0%");

        // Wait for a live SOCKS port (bootstrap to 100% can take a while on
        // first run while the consensus downloads; the proxy answers earlier).
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException("tor.exe exited during startup.");
            }
            if (await TorProxy.IsAvailableAsync(socksHost, socksPort).ConfigureAwait(false))
            {
                StatusChanged?.Invoke(this, BootstrapPercent >= 100 ? "ready" : $"starting {BootstrapPercent}%");
                return;
            }
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        Stop();
        throw new TimeoutException("Tor did not open its SOCKS port in time.");
    }

    public void Stop()
    {
        BootstrapPercent = 0;
        var process = Interlocked.Exchange(ref _process, null);
        if (process == null) return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // Already gone.
        }
        finally
        {
            try { process.Dispose(); } catch { }
        }

        StatusChanged?.Invoke(this, "stopped");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
