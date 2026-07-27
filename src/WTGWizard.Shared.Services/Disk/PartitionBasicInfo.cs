namespace WTGWizard.Shared.Services.Disk;

/// <summary>
/// 分区基本信息 — 简化数据模型，供服务层使用。
/// </summary>
public sealed record PartitionBasicInfo(
    uint DiskNumber,
    uint PartitionNumber,
    ulong Size,
    string? DriveLetter,
    string? VolumeLabel);
