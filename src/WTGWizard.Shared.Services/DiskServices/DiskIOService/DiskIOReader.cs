using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Vanara.PInvoke;
using WTGWizard.Shared.Services.Logger;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.SetupAPI;

namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 磁盘读取器 — 封装所有磁盘查询操作。
/// </summary>
internal sealed class DiskIOReader
{
    private readonly ILoggerService _logger;

    private static readonly Guid GUID_DEVINTERFACE_DISK = new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

    internal DiskIOReader(ILoggerService logger)
    {
        _logger = logger;
    }

    /// <summary>枚举外部磁盘。</summary>
    internal Task<IReadOnlyList<DiskBasicInfo>> EnumerateExternalDisksAsync(CancellationToken ct = default)
    {
        var disks = new List<DiskBasicInfo>();

        try
        {
            using var hDevInfo = SetupDiGetClassDevs(
                GUID_DEVINTERFACE_DISK,
                null,
                HWND.NULL,
                DIGCF.DIGCF_PRESENT | DIGCF.DIGCF_DEVICEINTERFACE);

            if (hDevInfo.IsInvalid)
            {
                _logger.Error("DiskIOReader", "EnumerateExternalDisks: SetupDiGetClassDevs failed - ({Error}).", Marshal.GetLastWin32Error());
                return Task.FromResult<IReadOnlyList<DiskBasicInfo>>(disks);
            }

            var ifaceData = new SP_DEVICE_INTERFACE_DATA();
            ifaceData.cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();
            for (uint i = 0; SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, GUID_DEVINTERFACE_DISK, i, ref ifaceData); i++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!SetupDiGetDeviceInterfaceDetail(hDevInfo, ifaceData, out string? devicePath, out var devInfoData))
                        continue;

                    if (string.IsNullOrEmpty(devicePath)) continue;

                    var diskInfo = QueryDiskInfo(devicePath, hDevInfo, devInfoData);
                    if (diskInfo is not null)
                    {
                        disks.Add(diskInfo);
                        _logger.Debug("DiskIOReader", "EnumerateExternalDisks: Found disk: {Index} - {Model}", diskInfo.Index, diskInfo.Model);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("DiskIOReader", "EnumerateExternalDisks: Device {Index} failed - ({Error}).", i, ex.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("DiskIOReader", "EnumerateExternalDisks: Method failed - ({Error}).", ex.ToString());
        }

        _logger.Info("DiskIOReader", "EnumerateExternalDisks done, enumerated disks: {Count}", disks.Count);
        return Task.FromResult<IReadOnlyList<DiskBasicInfo>>(disks);
    }

    /// <summary>检查磁盘安全性。</summary>
    internal Task<string?> CheckDiskSafetyAsync(string diskDeviceId, CancellationToken ct = default)
    {
        _logger.Info("DiskIOReader", "CheckDiskSafety: Checking safety of disk {DiskDeviceId}", diskDeviceId);
        ct.ThrowIfCancellationRequested();

        uint targetDiskIndex = ParseDiskNumber(diskDeviceId);
        if (targetDiskIndex == uint.MaxValue)
        {
            _logger.Error("DiskIOReader", "CheckDiskSafety: Invalid diskDeviceId={DiskDeviceId}", diskDeviceId);
            return Task.FromResult<string?>(null);
        }

        string systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "")
            .TrimEnd(':').ToUpperInvariant();
        HashSet<string> pageFileDrives = GetPageFileDrives();

        try
        {
            var allVolumes = EnumerateAllVolumes();
            _logger.Debug("DiskIOReader", "CheckDiskSafety: Disk volume count={Count}", allVolumes.Count);

            foreach (string volumeGuid in allVolumes)
            {
                ct.ThrowIfCancellationRequested();

                var extents = GetVolumeDiskExtents(volumeGuid);
                bool onTarget = extents.Any(e => e.diskNumber == targetDiskIndex);

                if (!onTarget) continue;

                string? driveLetter = GetFirstDriveLetter(volumeGuid);
                if (driveLetter is null) continue;

                string upper = driveLetter.ToUpperInvariant();

                // 检测系统卷
                if (string.Equals(upper, systemDrive, StringComparison.Ordinal))
                {
                    _logger.Warn("DiskIOReader", "CheckDiskSafety: System volume ({Drive}:) detected on disk {DiskIndex}!", driveLetter, targetDiskIndex);
                    return Task.FromResult<string?>($"System volume ({driveLetter}:) detected on disk {targetDiskIndex}!");
                }

                // 检测页面文件卷
                if (pageFileDrives.Contains(upper))
                {
                    _logger.Warn("DiskIOReader", "CheckDiskSafety: Page file volume ({Drive}:) detected on disk {DiskIndex}!", driveLetter, targetDiskIndex);
                    return Task.FromResult<string?>($"Page file volume ({driveLetter}:) detected on disk {targetDiskIndex}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("DiskIOReader", "CheckDiskSafety: Method failed - ({Error}).", ex.ToString());
        }

        _logger.Info("DiskIOReader", "CheckDiskSafety: Disk {Index} is safe for operations.", targetDiskIndex);
        return Task.FromResult<string?>(null);
    }

    /// <summary>获取磁盘分区列表。</summary>
    internal Task<IReadOnlyList<PartitionBasicInfo>> GetPartitionsAsync(uint diskIndex, bool skipEsp = true, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var partitions = new List<PartitionBasicInfo>();

        _logger.Debug("DiskIOReader", "GetPartitionsAsync: disk={DiskIndex}, skipEsp={SkipEsp}", diskIndex, skipEsp);

        try
        {
            // 1. 获取磁盘布局条目
            var layoutEntries = GetDriveLayoutEntries(diskIndex);
            _logger.Debug("DiskIOReader", "GetPartitionsAsync: layoutEntries count={Count}", layoutEntries.Count);

            // 2. 枚举所有卷
            var allVolumes = EnumerateAllVolumes();
            _logger.Debug("DiskIOReader", "GetPartitionsAsync: allVolumes count={Count}", allVolumes.Count);

            // 3. 通过磁盘 extents 匹配分区
            foreach (string volumeGuid in allVolumes)
            {
                ct.ThrowIfCancellationRequested();

                // 获取卷的磁盘 extents
                var extents = GetVolumeDiskExtents(volumeGuid);

                // 单次遍历：计算卷在目标磁盘上的大小 + 记录首个 extent 偏移
                ulong totalSizeOnDisk = 0;
                long matchOffset = -1;
                foreach (var ext in extents)
                {
                    if (ext.diskNumber != diskIndex) continue;
                    totalSizeOnDisk += (ulong)ext.extentLength;
                    if (matchOffset < 0) matchOffset = ext.startingOffset;
                }

                // 卷不在目标磁盘：单行合并（跳过卷日志精简）
                if (totalSizeOnDisk == 0)
                {
                    _logger.Debug("DiskIOReader", "GetPartitionsAsync: volume={VolumeGuid} - Not on target disk.", volumeGuid);
                    continue;
                }

                // 跳过小卷（可能是 ESP）
                if (skipEsp && totalSizeOnDisk < (ulong)DiskConstants.BytesPerGiB) continue;

                _logger.Debug("DiskIOReader", "GetPartitionsAsync: volume={VolumeGuid} - totalSizeOnDisk={Size} bytes", volumeGuid, totalSizeOnDisk);
                _logger.Debug("DiskIOReader", "GetPartitionsAsync: volume={VolumeGuid} - matchOffset={MatchOffset}", volumeGuid, matchOffset);

                // 通过 StartingOffset 匹配分区（前置守卫保证 matchOffset 有效）
                var matchedEntry = layoutEntries.FirstOrDefault(e => e.startingOffset == matchOffset);

                _logger.Debug("DiskIOReader", "GetPartitionsAsync: volume={VolumeGuid} - partNum={PartNum}, isEsp={IsEsp}", volumeGuid, matchedEntry.partitionNumber, matchedEntry.isEsp);

                // 跳过 ESP 分区
                if (skipEsp && matchedEntry.isEsp) continue;

                // 获取驱动器字母和卷标
                string? driveLetter = GetFirstDriveLetter(volumeGuid);
                string? volumeLabel = GetVolumeLabel(volumeGuid);

                _logger.Debug("DiskIOReader", "GetPartitionsAsync: volume={VolumeGuid} - driveLetter={DriveLetter}, volumeLabel={VolumeLabel}", volumeGuid, driveLetter, volumeLabel);

                // 添加到分区列表
                partitions.Add(new PartitionBasicInfo(
                    DiskNumber: diskIndex,
                    PartitionNumber: matchedEntry.partitionNumber,
                    Size: totalSizeOnDisk,
                    DriveLetter: driveLetter,
                    VolumeLabel: volumeLabel));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("DiskIOReader", "GetPartitionsAsync: Method failed - ({Error}).", ex.ToString());
        }

        _logger.Info("DiskIOReader", "GetPartitionsAsync: Disk {Index} returned {Count} partitions.", diskIndex, partitions.Count);
        return Task.FromResult<IReadOnlyList<PartitionBasicInfo>>(partitions);
    }

    // ══════════════════════════════════════════════════════
    //  辅助方法 - 系统信息
    // ══════════════════════════════════════════════════════

    /// <summary>从设备 ID 解析磁盘号。</summary>
    private static uint ParseDiskNumber(string diskDeviceId)
    {
        // 格式如 "\\.\PhysicalDrive0"
        var match = System.Text.RegularExpressions.Regex.Match(diskDeviceId, @"\d+$");
        if (match.Success && uint.TryParse(match.Value, out uint diskIndex))
        {
            return diskIndex;
        }
        return uint.MaxValue;
    }

    /// <summary>获取包含页面文件的驱动器字母集合。</summary>
    private static HashSet<string> GetPageFileDrives()
    {
        var drives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // 读取注册表获取页面文件位置
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            if (key is not null)
            {
                var pageFile = key.GetValue("PagingFiles") as string[];
                if (pageFile is not null)
                {
                    foreach (var file in pageFile)
                    {
                        // 格式如 "C:\pagefile.sys ..."
                        if (file.Length >= 2 && file[1] == ':')
                        {
                            drives.Add(file[0].ToString().ToUpperInvariant());
                        }
                    }
                }
            }
        }
        catch
        {
            // 忽略读取失败
        }

        return drives;
    }

    // ══════════════════════════════════════════════════════
    //  辅助方法 - 磁盘布局
    // ══════════════════════════════════════════════════════

    /// <summary>获取磁盘布局条目（分区号、起始偏移、是否 ESP）。</summary>
    private List<(uint partitionNumber, long startingOffset, bool isEsp)> GetDriveLayoutEntries(uint diskIndex)
    {
        var result = new List<(uint, long, bool)>();
        string devicePath = $@"\\.\PhysicalDrive{diskIndex}";

        _logger.Debug("DiskIOReader", "GetDriveLayoutEntries: disk={DiskIndex}", diskIndex);

        try
        {
            using var hDevice = CreateFile(
                devicePath,
                0,
                FileShare.ReadWrite,
                null,
                FileMode.Open,
                0);

            if (hDevice.IsInvalid)
            {
                _logger.Warn("DiskIOReader", "GetDriveLayoutEntries: device handle is invalid.");
                return result;
            }

            // 查询驱动器布局
            if (DeviceIoControl(hDevice, IOControlCode.IOCTL_DISK_GET_DRIVE_LAYOUT_EX,
                out DRIVE_LAYOUT_INFORMATION_EX layout))
            {
                for (int i = 0; i < layout.PartitionCount; i++)
                {
                    var entry = layout.PartitionEntry[i];
                    bool isEsp = entry.Gpt.PartitionType == DiskConstants.EspGptType;
                    // 通过实际分区号匹配磁盘分区
                    result.Add((entry.PartitionNumber, entry.StartingOffset, isEsp));
                }
            }

            _logger.Debug("DiskIOReader", "GetDriveLayoutEntries: disk={DiskIndex} - partitionCount={Count}", diskIndex, result.Count);
        }
        catch (Exception ex)
        {
            _logger.Warn("DiskIOReader", "GetDriveLayoutEntries: Method failed - ({Error}).", ex.ToString());
        }

        return result;
    }

    /// <summary>获取磁盘的 ESP 分区信息。</summary>
    internal (bool hasEsp, uint espPartitionNumber) GetEspPartitionInfo(uint diskIndex)
    {
        _logger.Debug("DiskIOReader", "GetEspPartitionInfo: disk={DiskIndex}", diskIndex);

        var layoutEntries = GetDriveLayoutEntries(diskIndex);
        var espEntry = layoutEntries.FirstOrDefault(e => e.isEsp);

        if (espEntry.partitionNumber == 0)
        {
            _logger.Debug("DiskIOReader", "GetEspPartitionInfo: disk={DiskIndex} - No ESP partition found.", diskIndex);
            return (false, 0);
        }

        _logger.Debug("DiskIOReader", "GetEspPartitionInfo: disk={DiskIndex} - espPartitionNumber={EspPartitionNumber}", diskIndex, espEntry.partitionNumber);
        return (true, espEntry.partitionNumber);
    }

    // ══════════════════════════════════════════════════════
    //  辅助方法 - 卷操作
    // ══════════════════════════════════════════════════════

    /// <summary>枚举系统上的所有卷。</summary>
    private List<string> EnumerateAllVolumes()
    {
        var volumes = new List<string>();

        try
        {
            StringBuilder volumeName = new StringBuilder(260);
            using var findHandle = FindFirstVolume(volumeName, (uint)volumeName.Capacity);

            if (!findHandle.IsInvalid)
            {
                do
                {
                    volumes.Add(volumeName.ToString());
                } while (FindNextVolume(findHandle, volumeName, (uint)volumeName.Capacity));
            }
        }
        catch (Exception)
        {
            // 忽略枚举失败
        }

        return volumes;
    }

    /// <summary>获取卷的磁盘 extents。</summary>
    private List<(uint diskNumber, long startingOffset, long extentLength)> GetVolumeDiskExtents(string volumeGuid)
    {
        var extents = new List<(uint, long, long)>();

        _logger.Debug("DiskIOReader", "GetVolumeDiskExtents: volume={VolumeGuid}", volumeGuid);

        try
        {
            // 移除末尾的反斜杠
            string volumePath = volumeGuid.TrimEnd('\\');

            using var hVolume = CreateFile(
                volumePath,
                0,
                FileShare.ReadWrite,
                null,
                FileMode.Open,
                0);

            if (hVolume.IsInvalid)
            {
                _logger.Warn("DiskIOReader", "GetVolumeDiskExtents: Volume handle is invalid.");
                return extents;
            }

            // 查询卷的磁盘 extents
            if (DeviceIoControl(hVolume, IOControlCode.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                out VOLUME_DISK_EXTENTS diskExtents))
            {
                for (int i = 0; i < diskExtents.NumberOfDiskExtents; i++)
                {
                    var extent = diskExtents.Extents[i];
                    extents.Add((extent.DiskNumber, extent.StartingOffset, extent.ExtentLength));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("DiskIOReader", "GetVolumeDiskExtents: Method failed - ({Error}).", ex.ToString());
        }

        return extents;
    }

    /// <summary>获取卷的第一个驱动器字母。</summary>
    private string? GetFirstDriveLetter(string volumeGuid)
    {
        try
        {
            if (GetVolumePathNamesForVolumeName(volumeGuid, out string[] paths))
            {
                var driveLetter = paths
                    .FirstOrDefault(p => p.Length >= 2 && p[1] == ':')?[..1];
                _logger.Debug("DiskIOReader", "GetFirstDriveLetter: volume={VolumeGuid} - letter={Letter}", volumeGuid, driveLetter);
                return driveLetter;
            }
        }
        catch (Exception)
        {
            // 忽略获取失败
        }

        _logger.Debug("DiskIOReader", "GetFirstDriveLetter: volume={VolumeGuid} - No letter found.", volumeGuid);
        return null;
    }

    /// <summary>获取卷标。</summary>
    private string? GetVolumeLabel(string volumeGuid)
    {
        try
        {
            StringBuilder volumeName = new StringBuilder(260);

            if (GetVolumeInformation(
                volumeGuid,
                volumeName, volumeName.Capacity,
                out _,
                out _,
                out _,
                null, 0))
            {
                return volumeName.ToString();
            }
        }
        catch (Exception)
        {
            // 忽略获取失败
        }

        return null;
    }

    // ══════════════════════════════════════════════════════
    //  辅助方法 - 磁盘枚举
    // ══════════════════════════════════════════════════════

    private DiskBasicInfo? QueryDiskInfo(string devicePath, SafeHDEVINFO hDevInfo, SP_DEVINFO_DATA devInfoData)
    {
        _logger.Debug("DiskIOReader", "QueryDiskInfo: devicePath={DevicePath}", devicePath);

        try
        {
            using var hDevice = CreateFile(
                devicePath,
                0,
                FileShare.ReadWrite,
                null,
                FileMode.Open,
                0);

            if (hDevice.IsInvalid)
            {
                _logger.Debug("DiskIOReader", "QueryDiskInfo: Device handle is invalid.");
                return null;
            }

            // 查询设备号
            uint diskIndex = QueryDeviceNumber(hDevice);
            if (diskIndex == uint.MaxValue)
            {
                _logger.Warn("DiskIOReader", "QueryDiskInfo: Failed to get device number.");
                return null;
            }

            // 查询总线类型
            var busType = QueryStorageBusType(hDevice, out bool removable);
            bool isVirtual = IsVirtualDisk(busType, devicePath);
            bool isExternal = IsExternalBusType(busType);

            if (!isExternal && !isVirtual)
            {
                _logger.Debug("DiskIOReader", "QueryDiskInfo: Disk {Index} is not external or virtual.", diskIndex);
                return null;
            }

            // 查询磁盘大小
            ulong sizeBytes = QueryDiskSize(hDevice);

            // 获取设备名称
            string model = GetDeviceFriendlyName(hDevInfo, devInfoData);
            var (mediaType, interfaceType) = MapBusType(busType, removable);

            // 检测 ESP 分区
            var (hasEsp, espPartitionNumber) = GetEspPartitionInfo(diskIndex);

            _logger.Info("DiskIOReader", "QueryDiskInfo: disk={Index}, model={Model}, size={Size}GB, busType={BusType}, hasEsp={HasEsp}",
                diskIndex, model, sizeBytes / (1024.0 * 1024 * 1024), busType, hasEsp);

            return new DiskBasicInfo(
                Index: diskIndex,
                DeviceId: $@"\\.\PhysicalDrive{diskIndex}",
                Model: model,
                SizeBytes: sizeBytes,
                MediaType: mediaType,
                InterfaceType: interfaceType,
                IsVirtualDisk: isVirtual,
                HasEspPartition: hasEsp,
                EspPartitionNumber: espPartitionNumber);
        }
        catch (Exception ex)
        {
            _logger.Warn("DiskIOReader", "QueryDiskInfo: Method failed - ({Error}).", ex.ToString());
            return null;
        }
    }

    private static uint QueryDeviceNumber(SafeHFILE hDevice)
    {
        if (DeviceIoControl(hDevice, IOControlCode.IOCTL_STORAGE_GET_DEVICE_NUMBER, out STORAGE_DEVICE_NUMBER deviceNumber))
            return deviceNumber.DeviceNumber;
        return uint.MaxValue;
    }

    private static STORAGE_BUS_TYPE QueryStorageBusType(SafeHFILE hDevice, out bool removableMedia)
    {
        removableMedia = false;
        var query = new STORAGE_PROPERTY_QUERY(STORAGE_PROPERTY_ID.StorageDeviceProperty);
        if (DeviceIoControl(hDevice, IOControlCode.IOCTL_STORAGE_QUERY_PROPERTY, query, out STORAGE_DESCRIPTOR_HEADER header))
        {
            if (DeviceIoControl(hDevice, IOControlCode.IOCTL_STORAGE_QUERY_PROPERTY, query, out STORAGE_DEVICE_DESCRIPTOR_MGD descriptor, header.Size))
            {
                removableMedia = descriptor.RemovableMedia;
                return descriptor.BusType;
            }
        }
        return STORAGE_BUS_TYPE.BusTypeUnknown;
    }

    private static ulong QueryDiskSize(SafeHFILE hDevice)
    {
        if (DeviceIoControl(hDevice, IOControlCode.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, out DISK_GEOMETRY_EX geometry))
            return (ulong)geometry.DiskSize;
        return 0;
    }

    private static string GetDeviceFriendlyName(SafeHDEVINFO hDevInfo, SP_DEVINFO_DATA devInfoData)
    {
        try
        {
            // 第一次调用获取缓冲区大小
            SetupDiGetDeviceRegistryProperty(hDevInfo, devInfoData, SPDRP.SPDRP_FRIENDLYNAME,
                out _, IntPtr.Zero, 0, out uint requiredSize);

            if (requiredSize > 0)
            {
                // 分配缓冲区并获取属性
                IntPtr buffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    if (SetupDiGetDeviceRegistryProperty(hDevInfo, devInfoData, SPDRP.SPDRP_FRIENDLYNAME,
                        out _, buffer, requiredSize, out _))
                    {
                        return Marshal.PtrToStringAuto(buffer) ?? "Unknown";
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch { }

        return "Unknown";
    }

    private static bool IsExternalBusType(STORAGE_BUS_TYPE busType)
    {
        return busType switch
        {
            STORAGE_BUS_TYPE.BusTypeUsb => true,
            STORAGE_BUS_TYPE.BusType1394 => true,
            STORAGE_BUS_TYPE.BusTypeSd => true,
            STORAGE_BUS_TYPE.BusTypeMmc => true,
            STORAGE_BUS_TYPE.BusTypeVirtual => true,
            STORAGE_BUS_TYPE.BusTypeFileBackedVirtual => true,
            _ => false
        };
    }

    private static bool IsVirtualDisk(STORAGE_BUS_TYPE busType, string devicePath)
    {
        if (busType is STORAGE_BUS_TYPE.BusTypeVirtual or STORAGE_BUS_TYPE.BusTypeFileBackedVirtual)
            return true;

        return devicePath.Contains("Virtual Disk", StringComparison.OrdinalIgnoreCase);
    }

    private static (string mediaType, string interfaceType) MapBusType(STORAGE_BUS_TYPE busType, bool removable)
    {
        string mediaType = busType switch
        {
            STORAGE_BUS_TYPE.BusTypeUsb when removable => "Removable Media",
            STORAGE_BUS_TYPE.BusTypeUsb => "External hard disk media",
            STORAGE_BUS_TYPE.BusType1394 => "External hard disk media",
            STORAGE_BUS_TYPE.BusTypeSd => "Removable Media",
            STORAGE_BUS_TYPE.BusTypeMmc => "Removable Media",
            STORAGE_BUS_TYPE.BusTypeVirtual => "Virtual Disk",
            STORAGE_BUS_TYPE.BusTypeFileBackedVirtual => "Virtual Disk",
            _ => "Unknown"
        };

        string interfaceType = busType switch
        {
            STORAGE_BUS_TYPE.BusTypeUsb => "USB",
            STORAGE_BUS_TYPE.BusType1394 => "IEEE 1394",
            STORAGE_BUS_TYPE.BusTypeSd => "SD",
            STORAGE_BUS_TYPE.BusTypeMmc => "MMC",
            STORAGE_BUS_TYPE.BusTypeVirtual or STORAGE_BUS_TYPE.BusTypeFileBackedVirtual => "Virtual",
            _ => "Unknown"
        };

        return (mediaType, interfaceType);
    }
}
