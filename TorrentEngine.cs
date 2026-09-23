using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;
using MonoTorrent.Client;

namespace IfredrixDownloadManager;

/// <summary>
/// Thin wrapper around MonoTorrent 3.x that maps torrent activity onto our
/// own <see cref="DownloadTask"/> model.
/// </summary>
public sealed class TorrentEngine : IDisposable
{
    private readonly ConcurrentDictionary<string, TorrentManager> _managers = new();
    private readonly string _cacheDirectory;
    private readonly ClientEngine _engine;
    private long _downloadLimit;
    private long _uploadLimit;

    public TorrentEngine(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(cacheDirectory);
        _engine = CreateEngine();
    }

    private ClientEngine CreateEngine()
    {
        EngineSettings Build(int port) => new EngineSettingsBuilder
        {
            CacheDirectory = _cacheDirectory,
            AllowPortForwarding = true,
            AllowLocalPeerDiscovery = true,
            AutoSaveLoadDhtCache = true,
            AutoSaveLoadFastResume = true,
            MaximumConnections = 250,
            MaximumHalfOpenConnections = 40,
            MaximumDownloadRate = ToIntRate(_downloadLimit),
            MaximumUploadRate = ToIntRate(_uploadLimit),
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                ["ipv4"] = new IPEndPoint(IPAddress.Any, port)
            }
        }.ToSettings();

        foreach (var port in new[] { 55123, 55124, 55125, 0 })
        {
            try
            {
                return new ClientEngine(Build(port));
            }
            catch
            {
                // Try the next port.
            }
        }

        return new ClientEngine();
    }

    private static int ToIntRate(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return 0;
        return bytesPerSecond > int.MaxValue ? int.MaxValue : (int)bytesPerSecond;
    }

    public TorrentManager? Get(string taskId)
        => _managers.TryGetValue(taskId, out var manager) ? manager : null;

    /// <summary>Reads name/size from a .torrent file without registering it.</summary>
    public static async Task<(string Name, long Size)> InspectAsync(string torrentPath)
    {
        var torrent = await Torrent.LoadAsync(torrentPath).ConfigureAwait(false);
        return (torrent.Name ?? Path.GetFileNameWithoutExtension(torrentPath), torrent.Size);
    }

    /// <summary>Reads name/size from a magnet URI without registering it.</summary>
    public static (string? Name, long? Size) InspectMagnet(string uri)
    {
        try
        {
            var magnet = MagnetLink.Parse(uri);
            return (magnet.Name, magnet.Size);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task<TorrentManager> RegisterAsync(string taskId, Func<Task<TorrentManager>> factory)
    {
        if (_managers.TryGetValue(taskId, out var existing)) return existing;

        var manager = await factory().ConfigureAwait(false);
        _managers[taskId] = manager;
        return manager;
    }

    /// <summary>Runs the torrent until it completes, is cancelled, or faults.</summary>
    public async Task DownloadAsync(DownloadTask task, CancellationToken ct)
    {
        Directory.CreateDirectory(task.SavePath);

        var isMagnet = task.Url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);

        var manager = await RegisterAsync(task.Id, async () => isMagnet
                ? await _engine.AddAsync(MagnetLink.Parse(task.Url), task.SavePath).ConfigureAwait(false)
                : await _engine.AddAsync(task.Url, task.SavePath).ConfigureAwait(false))
            .ConfigureAwait(false);

        if (!manager.HasMetadata)
        {
            await manager.WaitForMetadataAsync(ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(manager.Name))
        {
            task.FileName = manager.Name;
        }

        if (manager.Torrent is { } torrent && torrent.Size > 0)
        {
            task.FileSize = torrent.Size;
        }

        await manager.StartAsync().ConfigureAwait(false);

        while (!ct.IsCancellationRequested && !manager.Complete)
        {
            if (manager.State == TorrentState.Error)
            {
                throw new InvalidOperationException(
                    manager.Error?.Exception?.Message ?? "The torrent engine reported an error.");
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        if (manager.Complete)
        {
            await manager.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Copies live torrent statistics onto the task.</summary>
    public void UpdateStats(DownloadTask task)
    {
        var manager = Get(task.Id);
        if (manager == null) return;

        if (manager.Torrent is { } torrent && torrent.Size > 0)
        {
            task.FileSize = torrent.Size;
        }

        var monitor = manager.Monitor;
        task.DownloadSpeed = monitor?.DownloadRate ?? 0;
        task.UploadSpeed = monitor?.UploadRate ?? 0;
        task.Connections = manager.OpenConnections;

        if (task.FileSize > 0)
        {
            task.DownloadedBytes = (long)Math.Round(task.FileSize * manager.Progress / 100.0);
        }

        var remaining = task.FileSize - task.DownloadedBytes;
        task.TimeRemaining = task.DownloadSpeed > 1024 && remaining > 0
            ? TimeSpan.FromSeconds(remaining / task.DownloadSpeed)
            : null;
    }

    public async Task PauseAsync(string taskId)
    {
        if (Get(taskId) is { } manager)
        {
            try { await manager.PauseAsync().ConfigureAwait(false); } catch { /* ignore */ }
        }
    }

    public async Task RemoveAsync(string taskId, RemoveMode mode)
    {
        if (!_managers.TryRemove(taskId, out var manager)) return;
        try
        {
            await manager.StopAsync().ConfigureAwait(false);
            await _engine.RemoveAsync(manager, mode).ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }
    }

    public void SetSpeedLimit(long downloadBytesPerSecond, long uploadBytesPerSecond)
    {
        _downloadLimit = downloadBytesPerSecond;
        _uploadLimit = uploadBytesPerSecond;

        try
        {
            _ = _engine.UpdateSettingsAsync(new EngineSettingsBuilder(_engine.Settings)
            {
                MaximumDownloadRate = ToIntRate(downloadBytesPerSecond),
                MaximumUploadRate = ToIntRate(uploadBytesPerSecond)
            }.ToSettings());
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        try
        {
            var stop = _engine.StopAllAsync();
            if (!stop.Wait(TimeSpan.FromSeconds(5)))
            {
                // Fall back to a hard stop so the process can still exit.
            }
        }
        catch
        {
            // ignore
        }

        try { _engine.Dispose(); } catch { /* ignore */ }
    }
}
