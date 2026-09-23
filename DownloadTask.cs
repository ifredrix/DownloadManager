using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading;

namespace IfredrixDownloadManager;

public enum DownloadStatus
{
    Pending,
    Queued,
    Downloading,
    Paused,
    Completed,
    Error,
    Cancelled
}

public enum DownloadType
{
    Regular,
    Torrent,
    Stream
}

/// <summary>
/// One cookie the extension harvested for the page/media host. It travels
/// extension -&gt; local app -&gt; origin only (as a host-matched header or a
/// temporary Netscape jar that yt-dlp deletes after the call); the app never
/// writes cookies to history.json or any other file.
/// </summary>
public sealed class CookieEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;
    [JsonPropertyName("path")] public string Path { get; set; } = "/";
    [JsonPropertyName("hostOnly")] public bool HostOnly { get; set; }
    [JsonPropertyName("secure")] public bool Secure { get; set; }
    /// <summary>Unix seconds; 0 = session cookie.</summary>
    [JsonPropertyName("expires")] public double Expires { get; set; }
}

/// <summary>One transfer segment/connection snapshot for the progress window.</summary>
public sealed class SegmentStat
{
    public int Index { get; set; }
    public long Start { get; set; }
    public long Length { get; set; }
    public long Downloaded { get; set; }
    public bool Active { get; set; }
    public bool Done => Length > 0 && Downloaded >= Length;
}

public class DownloadTask : INotifyPropertyChanged
{
    private string _id;
    private string _url;
    private string _fileName;
    private string _savePath;
    private long _fileSize;
    private long _downloadedBytes;
    private DownloadStatus _status;
    private DownloadType _type;
    private double _downloadSpeed;
    private double _uploadSpeed;
    private TimeSpan? _timeRemaining;
    private string _errorMessage;
    private DateTime _createdAt;
    private DateTime? _completedAt;
    private string _category;
    private int _connections;
    private bool _supportsRange;
    private string _formatId = string.Empty;
    private string _referer = string.Empty;
    private string _ua = string.Empty;
    private List<CookieEntry>? _cookies;

    public DownloadTask()
    {
        _id = Guid.NewGuid().ToString("N")[..12];
        _url = string.Empty;
        _fileName = string.Empty;
        _savePath = string.Empty;
        _errorMessage = string.Empty;
        _category = "Other";
        _createdAt = DateTime.Now;
        _status = DownloadStatus.Pending;
        _type = DownloadType.Regular;
        _connections = 4;
        _supportsRange = false;
    }

    /// <summary>True when the server answered with "Accept-Ranges: bytes".</summary>
    public bool SupportsRange
    {
        get => _supportsRange;
        set { _supportsRange = value; OnPropertyChanged(nameof(SupportsRange)); }
    }

    /// <summary>Origin page (browser context) sent as HTTP Referer; empty = none.</summary>
    public string Referer
    {
        get => _referer;
        set { _referer = value; OnPropertyChanged(nameof(Referer)); }
    }

    /// <summary>Browser User-Agent (browser context); empty = app default.</summary>
    public string Ua
    {
        get => _ua;
        set { _ua = value ?? string.Empty; OnPropertyChanged(nameof(Ua)); }
    }

    /// <summary>Browser cookies for this URL's host; null = none. Memory only:
    /// never written to history.json or any other file.</summary>
    public List<CookieEntry>? Cookies
    {
        get => _cookies;
        set { _cookies = value; OnPropertyChanged(nameof(Cookies)); }
    }

    /// <summary>Thread-safe increment, used by the parallel HTTP chunk writers.</summary>
    public void AddDownloadedBytes(long count)
    {
        if (count <= 0) return;
        Interlocked.Add(ref _downloadedBytes, count);
    }

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(nameof(Id)); }
    }

    public string Url
    {
        get => _url;
        set { _url = value; OnPropertyChanged(nameof(Url)); }
    }

    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(nameof(FileName)); }
    }

    public string SavePath
    {
        get => _savePath;
        set { _savePath = value; OnPropertyChanged(nameof(SavePath)); }
    }

    public long FileSize
    {
        get => _fileSize;
        set { _fileSize = value; OnPropertyChanged(nameof(FileSize)); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(ProgressPercentage)); OnPropertyChanged(nameof(SizeText)); }
    }

    public long DownloadedBytes
    {
        get => Interlocked.Read(ref _downloadedBytes);
        set { Interlocked.Exchange(ref _downloadedBytes, value); OnPropertyChanged(nameof(DownloadedBytes)); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(ProgressPercentage)); }
    }

    public DownloadStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(SizeText)); }
    }

    public DownloadType Type
    {
        get => _type;
        set { _type = value; OnPropertyChanged(nameof(Type)); OnPropertyChanged(nameof(TypeText)); OnPropertyChanged(nameof(SizeText)); }
    }

    public double DownloadSpeed
    {
        get => _downloadSpeed;
        set { _downloadSpeed = value; OnPropertyChanged(nameof(DownloadSpeed)); OnPropertyChanged(nameof(FormattedDownloadSpeed)); }
    }

    public double UploadSpeed
    {
        get => _uploadSpeed;
        set { _uploadSpeed = value; OnPropertyChanged(nameof(UploadSpeed)); OnPropertyChanged(nameof(FormattedUploadSpeed)); }
    }

    public TimeSpan? TimeRemaining
    {
        get => _timeRemaining;
        set { _timeRemaining = value; OnPropertyChanged(nameof(TimeRemaining)); OnPropertyChanged(nameof(FormattedTimeRemaining)); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(nameof(ErrorMessage)); }
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set { _createdAt = value; OnPropertyChanged(nameof(CreatedAt)); OnPropertyChanged(nameof(FormattedCreatedAt)); }
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set { _completedAt = value; OnPropertyChanged(nameof(CompletedAt)); }
    }

    public string Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(nameof(Category)); OnPropertyChanged(nameof(CategoryText)); }
    }

    public int Connections
    {
        get => _connections;
        set { _connections = value; OnPropertyChanged(nameof(Connections)); }
    }

    private bool _useTor;

    /// <summary>True when this download must go through the Tor network.</summary>
    public bool UseTor
    {
        get => _useTor;
        set { _useTor = value; OnPropertyChanged(nameof(UseTor)); OnPropertyChanged(nameof(TypeText)); }
    }

    private bool _openFolderOnDone;

    /// <summary>Open the containing folder when this download finishes.</summary>
    public bool OpenFolderOnDone
    {
        get => _openFolderOnDone;
        set { _openFolderOnDone = value; OnPropertyChanged(nameof(OpenFolderOnDone)); }
    }

    /// <summary>yt-dlp format selector picked by the user (e.g. "399+140").
    /// Empty = automatic best. Stream tasks only.</summary>
    public string FormatId
    {
        get => _formatId;
        set { _formatId = value ?? string.Empty; OnPropertyChanged(nameof(FormatId)); }
    }

    private long _speedLimitBps;

    /// <summary>Per-download cap in bytes/s. 0 means "use the global limit".</summary>
    public long SpeedLimitBps
    {
        get => System.Threading.Interlocked.Read(ref _speedLimitBps);
        set
        {
            System.Threading.Interlocked.Exchange(ref _speedLimitBps, Math.Max(0, value));
            OnPropertyChanged(nameof(SpeedLimitBps));
            OnPropertyChanged(nameof(SpeedLimitText));
        }
    }

    /// <summary>Human-readable effective cap (per-download or global).</summary>
    public string SpeedLimitText => SpeedLimitBps > 0
        ? string.Format(Localization.T("pr.limitThis"), SpeedLimitBps / 1024.0)
        : Localization.T("pr.limitGlobal");

    // Computed properties
    public double Progress => FileSize > 0 ? (double)DownloadedBytes / FileSize : 0;
    public double ProgressPercentage => Progress * 100;
    public string StatusText => Localization.T("st." + Status);
    public string TypeText
    {
        get
        {
            if (Type != DownloadType.Regular) return Localization.T("ty." + Type);
            if (UseTor) return "Tor";
            if (Url.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase)) return "FTPS";
            if (Url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)) return "FTP";
            return Localization.T("ty.Regular");
        }
    }
    public string CategoryText => Localization.T("cat." + Category);
    public string FormattedDownloadSpeed => FormatSpeed(DownloadSpeed);
    public string FormattedUploadSpeed => FormatSpeed(UploadSpeed);

    /// <summary>Speed in the unit this task actually reports: bytes/s normally,
    /// percent/s for stream tasks (their yt-dlp callback only carries percent).</summary>
    public string FormattedSpeedDisplay => IsPercentProgress
        ? $"{DownloadSpeed:0.0} %/s"
        : FormattedDownloadSpeed;
    public string FormattedTimeRemaining => FormatTime(TimeRemaining);
    public string FormattedCreatedAt => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string FormattedFileSize => FormatBytes(FileSize);
    public string FormattedDownloadedBytes => FormatBytes(DownloadedBytes);

    /// <summary>True while a stream task still reports 0-100% progress on the
    /// FileSize=100 placeholder instead of real byte counts (until yt-dlp
    /// finishes and the true size is known).</summary>
    public bool IsPercentProgress =>
        Type == DownloadType.Stream && FileSize <= 100 && Status != DownloadStatus.Completed;

    /// <summary>Stream tasks map 0-100% onto FileSize=100: hide the fake size.</summary>
    public string SizeText => Type == DownloadType.Stream && Status != DownloadStatus.Completed
        ? "--"
        : FileSize > 0 ? FormattedFileSize : "--";

    private string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
            return $"{bytesPerSecond:F1} B/s";
        if (bytesPerSecond < 1024 * 1024)
            return $"{bytesPerSecond / 1024:F1} KB/s";
        if (bytesPerSecond < 1024 * 1024 * 1024)
            return $"{bytesPerSecond / (1024.0 * 1024):F1} MB/s";
        return $"{bytesPerSecond / (1024.0 * 1024 * 1024):F1} GB/s";
    }

    private string FormatTime(TimeSpan? timeSpan)
    {
        // A finished download has no "remaining time" at all: neutral dash
        // instead of a misleading "unknown".
        if (Status == DownloadStatus.Completed) return Localization.T("pr.none");
        if (!timeSpan.HasValue || timeSpan.Value.TotalSeconds < 0)
            return Localization.T("pr.unknown");

        var ts = timeSpan.Value;
        if (ts.TotalSeconds < 60)
            return $"{ts.Seconds}s";
        if (ts.TotalMinutes < 60)
            return $"{ts.Minutes}m {ts.Seconds}s";
        if (ts.TotalHours < 24)
            return $"{ts.Hours}h {ts.Minutes}m";
        return $"{ts.Days}d {ts.Hours}h";
    }

    private string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}