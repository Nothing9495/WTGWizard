namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 磁盘基本信息 — 简化数据模型，供服务层使用。
/// </summary>
public sealed record DiskBasicInfo(
    uint Index,
    string DeviceId,
    string Model,
    ulong SizeBytes,
    string MediaType,
    string InterfaceType,
    bool IsVirtualDisk,
    bool HasEspPartition,
    uint EspPartitionNumber);
