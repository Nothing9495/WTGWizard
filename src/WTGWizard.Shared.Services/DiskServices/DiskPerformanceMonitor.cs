using System;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using WTGWizard.Shared.Services.Logger;
using static Vanara.PInvoke.Kernel32;

namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 磁盘性能快照 — 读写速率、活动时间与平均响应时间。
/// </summary>
public sealed record DiskPerformanceSnapshot(
    double ReadBytesPerSec,
    double WriteBytesPerSec,
    double DiskBusyPercent,
    double? AvgResponseTimeMs)
{
    public string ReadDisplay => $"{ReadBytesPerSec / 1_048_576:F2} MB/s";
    public string WriteDisplay => $"{WriteBytesPerSec / 1_048_576:F2} MB/s";
    public string BusyDisplay => $"{DiskBusyPercent:F0}%";
    public string AvgResponseDisplay =>
        AvgResponseTimeMs is double v ? $"{v:F2} ms" : "-";
    public string ReadWriteDisplay => $"{ReadBytesPerSec / 1_048_576:F0} MB/s / {WriteBytesPerSec / 1_048_576:F0} MB/s";
}

/// <summary>
/// 基于 IOCTL_DISK_PERFORMANCE 的磁盘性能监控器。
/// 通过轮询累积计数器计算实时速率与平均响应时间。
/// 平均响应时间 = (ReadTime + WriteTime) / (ReadCount + WriteCount)，与任务管理器/PerfMon 的
/// "Avg. Disk sec/Transfer" 同源（PDH 底层即为这些计数器）。
/// 参考：https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-disk_performance
/// </summary>
public sealed class DiskPerformanceMonitor : IDisposable
{
    private readonly ILoggerService _logger;
    private readonly uint _diskNumber;
    private readonly object _lock = new();

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private DiskPerformanceSnapshot? _snapshot;
    private SafeHFILE? _deviceHandle;

    // 采样基线：缓存 IOCTL 计数器的上次采样值
    private bool _hasBaseline;
    private long _prevBytesRead;
    private long _prevBytesWritten;
    private long _prevIdleTime;
    private long _prevQueryTime;
    private long _prevReadTime;
    private long _prevWriteTime;
    private long _prevReadCount;
    private long _prevWriteCount;

    public DiskPerformanceSnapshot? CurrentSnapshot
    {
        get { lock (_lock) return _snapshot; }
    }

    public event Action<DiskPerformanceSnapshot>? Updated;

    public DiskPerformanceMonitor(uint diskNumber, ILoggerService logger)
    {
        _diskNumber = diskNumber;
        _logger = logger;
    }

    /// <summary>启动监控。</summary>
    public void Start(TimeSpan? interval = null)
    {
        if (_timer is not null) return;

        _deviceHandle = CreateFile(
            $@"\\.\PhysicalDrive{_diskNumber}",
            0,
            System.IO.FileShare.ReadWrite,
            null,
            System.IO.FileMode.Open,
            0);

        if (_deviceHandle.IsInvalid)
        {
            _logger.Warn("DiskPerformanceMonitor", "Cannot open disk {DiskNumber}", _diskNumber);
            return;
        }

        var pollInterval = interval ?? TimeSpan.FromSeconds(1);
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(pollInterval);
        _pollTask = PollAsync(_cts.Token);
    }

    /// <summary>停止监控。</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        _timer = null;
        _cts?.Dispose();
        _cts = null;
        _pollTask = null;

        _deviceHandle?.Dispose();
        _deviceHandle = null;
    }

    public void Dispose() => Stop();

    private async Task PollAsync(CancellationToken ct)
    {
        var timer = _timer!;

        try
        {
            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    var snapshot = QuerySnapshot();
                    if (snapshot is null) continue;

                    lock (_lock) _snapshot = snapshot;
                    Updated?.Invoke(snapshot);
                }
                catch (Exception ex)
                {
                    _logger.Warn("DiskPerformanceMonitor", "Sample failed for disk {DiskNumber}: {Error}", _diskNumber, ex.Message);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private DiskPerformanceSnapshot? QuerySnapshot()
    {
        if (_deviceHandle is null || _deviceHandle.IsInvalid) return null;

        // 使用 Vanara 的 DeviceIoControl 泛型版本
        if (!DeviceIoControl(_deviceHandle, IOControlCode.IOCTL_DISK_PERFORMANCE, out DISK_PERFORMANCE perf))
            return null;

        // 读取当前值
        long curBytesRead = perf.BytesRead;
        long curBytesWritten = perf.BytesWritten;
        long curIdleTime = perf.IdleTime;
        long curQueryTime = perf.QueryTime;
        long curReadTime = perf.ReadTime;
        long curWriteTime = perf.WriteTime;
        long curReadCount = perf.ReadCount;
        long curWriteCount = perf.WriteCount;

        // 首次采样：存储基线，返回 null
        if (!_hasBaseline)
        {
            _prevBytesRead = curBytesRead;
            _prevBytesWritten = curBytesWritten;
            _prevIdleTime = curIdleTime;
            _prevQueryTime = curQueryTime;
            _prevReadTime = curReadTime;
            _prevWriteTime = curWriteTime;
            _prevReadCount = curReadCount;
            _prevWriteCount = curWriteCount;
            _hasBaseline = true;
            return null;
        }

        // 计算差值
        long deltaQueryTime = curQueryTime - _prevQueryTime;
        if (deltaQueryTime <= 0)
        {
            _prevBytesRead = curBytesRead;
            _prevBytesWritten = curBytesWritten;
            _prevIdleTime = curIdleTime;
            _prevQueryTime = curQueryTime;
            _prevReadTime = curReadTime;
            _prevWriteTime = curWriteTime;
            _prevReadCount = curReadCount;
            _prevWriteCount = curWriteCount;
            return null;
        }

        double elapsedSec = deltaQueryTime / 10_000_000.0; // 100ns ticks → seconds
        double readBps = (curBytesRead - _prevBytesRead) / elapsedSec;
        double writeBps = (curBytesWritten - _prevBytesWritten) / elapsedSec;
        double busyPct = Math.Clamp((1.0 - (curIdleTime - _prevIdleTime) / (double)deltaQueryTime) * 100, 0, 100);

        // 平均响应时间（与任务管理器/PerfMon "Avg. Disk sec/Transfer" 同源）：
        // avg = (ReadTime + WriteTime) / (ReadCount + WriteCount)，100ns ticks → ms（除以 1e4）。
        // 区间内无 I/O 时返回 0（空闲）。
        double? avgResponseMs = null;
        long deltaReadCount = curReadCount - _prevReadCount;
        long deltaWriteCount = curWriteCount - _prevWriteCount;
        long deltaIO = deltaReadCount + deltaWriteCount;
        if (deltaIO > 0)
        {
            long deltaTime = (curReadTime - _prevReadTime) + (curWriteTime - _prevWriteTime);
            avgResponseMs = deltaTime / (double)deltaIO / 10_000.0;
        }
        else
        {
            avgResponseMs = 0; // 空闲 → 0.00 ms
        }

        // 更新基线
        _prevBytesRead = curBytesRead;
        _prevBytesWritten = curBytesWritten;
        _prevIdleTime = curIdleTime;
        _prevQueryTime = curQueryTime;
        _prevReadTime = curReadTime;
        _prevWriteTime = curWriteTime;
        _prevReadCount = curReadCount;
        _prevWriteCount = curWriteCount;

        return new DiskPerformanceSnapshot(readBps, writeBps, busyPct, avgResponseMs);
    }
}
