namespace WTGWizard.Main.DeploymentCore.Models;

/// <summary>
/// 部署配置快照 — 从 ViewModel 一次性提取的不可变数据记录。
/// 服务层通过此对象获取配置，不再直接依赖 ViewModel。
/// </summary>
public sealed record DeploymentConfig
{
    // ── 映像 ──
    public required string SrcImageFile { get; init; }
    public required int ImageSelectedIndex { get; init; }
    public required string ImageWindowsArch { get; init; }
    public required string ImageWinBuildNum { get; init; }
    public required double ImageExpandedSize { get; init; }
    // ── 磁盘 ──
    public required int DiskSelectedId { get; init; }
    public required ulong DiskSizeBytes { get; init; }
    public required bool IsCleanInstall { get; init; }

    // ── 分区（Clean 模式）──
    public required int EfiPartSize { get; init; }
    public required double OsDriveSize { get; init; }
    public required string OsDriveLabel { get; init; }
    public required bool EnableReservedVol { get; init; }
    public required string ReservedDriveLabel { get; init; }
    public required string ReservedDriveFs { get; init; }
    public required bool NoDefaultDriveLetter { get; init; }
    public required bool AutoRemoveOsDriveLetter { get; init; }
    public required double MaxOsDriveSize { get; init; }

    // ── 分区（Partition Install 模式）──
    public required uint EspVolumeId { get; init; }
    public required uint OsDriveVolumeId { get; init; }
    public required string? SelectedPartitionDriveLetter { get; init; }

    // ── 盘符 ──
    public required char EspDriveLetter { get; set; }
    public required char OsDriveLetter { get; set; }

    // ── 驱动集成 ──
    public required bool DriverIntegrationEnabled { get; init; }
    public required string? DriversDirectoryPath { get; init; }
    public required bool ForceUnsignedDriver { get; init; }

    // ── 应答文件 ──
    public required bool CustomAnsFileEnabled { get; init; }
    public required string? AnsFilePath { get; init; }
    public required bool CleanImageAnsFile { get; init; }

    // ── WTG 设置 ──
    public required bool HideLocalDisks { get; init; }
    public required bool PreventDeviceEncryption { get; init; }
    public required bool UseDismToDeploy { get; init; }

    // ── BCDBoot ──
    public required bool EnableBootEx { get; init; }
    public required bool EnableBootVerbose { get; init; }
}
