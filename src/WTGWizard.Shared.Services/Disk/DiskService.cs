using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using WTGWizard.Shared.Services.Logger;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.SetupAPI;

namespace WTGWizard.Shared.Services.Disk;

/// <summary>
/// 基于 Vanara 的磁盘服务实现。
/// </summary>
public sealed class DiskService : IDiskService
{
    private readonly ILoggerService _logger;

    private static readonly Guid GUID_DEVINTERFACE_DISK = new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

    public DiskService(ILoggerService logger)
    {
        _logger = logger;
    }

    /// <summary>枚举外部磁盘。</summary>
    public Task<IReadOnlyList<DiskBasicInfo>> EnumerateExternalDisksAsync(CancellationToken ct = default)
    {
        _logger.Debug("DiskService", "EnumerateExternalDisks start");
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
                _logger.Error("DiskService", "SetupDiGetClassDevs failed");
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
                        _logger.Debug("DiskService", "Found disk: {Index} {Model}", diskInfo.Index, diskInfo.Model);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("DiskService", "Device {Index} failed: {Error}", i, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("DiskService", "EnumerateExternalDisks failed: {Error}", ex.Message);
        }

        _logger.Debug("DiskService", "EnumerateExternalDisks done, count={Count}", disks.Count);
        return Task.FromResult<IReadOnlyList<DiskBasicInfo>>(disks);
    }

    /// <summary>检查磁盘安全性。</summary>
    public Task<string?> CheckDiskSafetyAsync(string diskDeviceId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // TODO: 实现磁盘安全检测
        return Task.FromResult<string?>(null);
    }

    /// <summary>获取磁盘分区列表。</summary>
    public Task<IReadOnlyList<PartitionBasicInfo>> GetPartitionsAsync(uint diskIndex, bool skipEsp = true, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var partitions = new List<PartitionBasicInfo>();

        // TODO: 使用 Vanara 实现分区查询
        _logger.Debug("DiskService", "GetPartitions for disk {Index}", diskIndex);
        return Task.FromResult<IReadOnlyList<PartitionBasicInfo>>(partitions);
    }

    // ══════════════════════════════════════════════════════
    //  辅助方法
    // ══════════════════════════════════════════════════════

    private DiskBasicInfo? QueryDiskInfo(string devicePath, SafeHDEVINFO hDevInfo, SP_DEVINFO_DATA devInfoData)
    {
        try
        {
            using var hDevice = CreateFile(
                devicePath,
                0,
                FileShare.ReadWrite,
                null,
                FileMode.Open,
                0);

            if (hDevice.IsInvalid) return null;

            // 查询设备号
            uint diskIndex = QueryDeviceNumber(hDevice);
            if (diskIndex == uint.MaxValue) return null;

            // 查询总线类型
            var busType = QueryStorageBusType(hDevice, out bool removable);
            bool isVirtual = IsVirtualDisk(busType, devicePath);
            bool isExternal = IsExternalBusType(busType);

            if (!isExternal && !isVirtual) return null;

            // 查询磁盘大小
            var (sizeBytes, _, _) = QueryDiskGeometry(hDevice);

            // 获取设备名称
            string model = GetDeviceFriendlyName(hDevInfo, devInfoData);
            var (mediaType, interfaceType) = MapBusType(busType, removable);

            return new DiskBasicInfo(
                Index: diskIndex,
                DeviceId: $@"\\.\PhysicalDrive{diskIndex}",
                Model: model,
                SizeBytes: sizeBytes,
                MediaType: mediaType,
                InterfaceType: interfaceType,
                IsVirtualDisk: isVirtual,
                HasEspPartition: false, // TODO: 检测 ESP
                EspPartitionNumber: 0);
        }
        catch (Exception ex)
        {
            _logger.Warn("DiskService", "QueryDiskInfo failed: {Error}", ex.Message);
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

    private static (ulong sizeBytes, ulong totalSectors, uint bytesPerSector) QueryDiskGeometry(SafeHFILE hDevice)
    {
        if (DeviceIoControl(hDevice, IOControlCode.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, out DISK_GEOMETRY_EX geometry))
        {
            ulong sizeBytes = (ulong)geometry.DiskSize;
            uint bps = geometry.Geometry.BytesPerSector;
            ulong totalSectors = bps > 0 ? sizeBytes / bps : 0;
            return (sizeBytes, totalSectors, bps);
        }
        return (0, 0, 512);
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
