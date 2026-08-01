using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using WTGWizard.Shared.Services.Logger;
using static Vanara.PInvoke.CfgMgr32;
using static Vanara.PInvoke.SetupAPI;

namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 磁盘/分区/卷设备变更监视器。
/// - CM_Register_Notification：磁盘+卷设备接口（即时，USB 插拔/分区创建删除）
/// - 轮询盘符映射：检测挂载点变更（2.5s 间隔，盘符分配/删除）
/// </summary>
internal sealed class DiskIOWatcher : IDisposable
{
    private readonly ILoggerService _logger;

    private SafeHCMNOTIFICATION? _diskNotifyHandle;
    private SafeHCMNOTIFICATION? _volumeNotifyHandle;
    private CM_NOTIFY_CALLBACK? _callback;

    private Timer? _mountPointPoller;
    private HashSet<string>? _lastDriveLetters;

    private int _debounceGen;

    /// <summary>磁盘或盘符发生变化时触发。</summary>
    internal event Action? DisksChanged;

    internal DiskIOWatcher(ILoggerService logger)
    {
        _logger = logger;
    }

    /// <summary>启动监视。</summary>
    internal void Start()
    {
        if (_diskNotifyHandle is not null) return;

        _callback = OnDeviceNotification;

        // 1. 磁盘设备接口通知（USB 插拔、虚拟磁盘挂载）
        RegisterNotification(GUID_DEVINTERFACE_DISK, ref _diskNotifyHandle, "Disk");

        // 2. 卷设备接口通知（分区创建/删除）
        RegisterNotification(GUID_DEVINTERFACE_VOLUME, ref _volumeNotifyHandle, "Volume");

        // 3. 盘符映射轮询（检测挂载点变更，因为 WM_DEVICECHANGE 不覆盖盘符操作）
        _lastDriveLetters = GetCurrentDriveLetters();
        _mountPointPoller = new Timer(_ => PollMountPoints(), null, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(2.5));

        _logger.Debug("DiskIOWatcher", "Start to monitor disk/volume/mountpoint changes.",
            string.Join(",", _lastDriveLetters.Order()));
    }

    /// <summary>停止监视。</summary>
    internal void Stop()
    {
        _logger.Debug("DiskIOWatcher", "Stop monitoring disk/volume/mountpoint changes.");
        Interlocked.Increment(ref _debounceGen);

        _mountPointPoller?.Dispose();
        _mountPointPoller = null;

        _diskNotifyHandle?.Dispose();
        _diskNotifyHandle = null;

        _volumeNotifyHandle?.Dispose();
        _volumeNotifyHandle = null;

        _callback = null;
    }

    // ════════════════════════════════════════════════════════════
    //  注册
    // ════════════════════════════════════════════════════════════

    private void RegisterNotification(Guid classGuid, ref SafeHCMNOTIFICATION? handle, string label)
    {
        handle = null;

        try
        {
            var filter = new CM_NOTIFY_FILTER(classGuid);

            var cr = CM_Register_Notification(
                in filter,
                IntPtr.Zero,
                _callback!,
                out var notifyHandle);

            if (cr != CONFIGRET.CR_SUCCESS)
            {
                _logger.Error("DiskIOWatcher", "RegisterNotification: Failed to register {Label} change notification - ({Error}).", label, cr);
            }
            else
            {
                handle = notifyHandle;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("DiskIOWatcher", "RegisterNotification: Unexpected error while registering {Label} change notification - ({Error}).", label, ex.Message);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  回调
    // ════════════════════════════════════════════════════════════

    private Win32Error OnDeviceNotification(HCMNOTIFICATION notify, IntPtr context, CM_NOTIFY_ACTION action, IntPtr eventData, uint eventDataSize)
    {
        try
        {
            if (action is not (CM_NOTIFY_ACTION.CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL or CM_NOTIFY_ACTION.CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL))
                return Win32Error.ERROR_SUCCESS;

            TriggerDebounced();
        }
        catch (Exception ex)
        {
            _logger.Warn("DiskIOWatcher", "OnDeviceNotification: Notification callback failed - ({Error}).", ex.Message);
        }
        return Win32Error.ERROR_SUCCESS;
    }

    // ════════════════════════════════════════════════════════════
    //  盘符轮询
    // ════════════════════════════════════════════════════════════

    private void PollMountPoints()
    {
        try
        {
            var current = GetCurrentDriveLetters();
            var previous = Interlocked.Exchange(ref _lastDriveLetters, current);

            if (previous is not null && !current.SetEquals(previous))
            {
                _logger.Debug("DiskIOWatcher", "PollMountPoints: Drive letters changed: [{Previous}] → [{Current}].",
                    string.Join(",", previous.Order()), string.Join(",", current.Order()));
                TriggerDebounced();
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("DiskIOWatcher", "PollMountPoints: PollMountPoints failed - ({Error}).", ex.Message);
        }
    }

    private static HashSet<string> GetCurrentDriveLetters()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType != DriveType.NoRootDirectory
                         && d.Name is { Length: >= 1 }
                         && char.IsLetter(d.Name[0]))
                .Select(d => d.Name[0].ToString().ToUpperInvariant())
                .ToHashSet();
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    // ════════════════════════════════════════════════════════════
    //  防抖
    // ════════════════════════════════════════════════════════════

    private async void TriggerDebounced()
    {
        try
        {
            var seq = Interlocked.Increment(ref _debounceGen);
            await Task.Delay(1000);
            if (seq != Volatile.Read(ref _debounceGen)) return;
            DisksChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Warn("DiskIOWatcher", "TriggerDebounced: Method failed - ({Error}).", ex.Message);
        }
    }

    public void Dispose() => Stop();
}
