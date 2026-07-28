using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 磁盘写入器 — 封装所有磁盘写操作。
/// TODO: 未来基于 Vanara PInvoke 实现以下操作：
/// - ClearDisk (IOCTL_DISK_CREATE_DISK, RAW) — 清除磁盘布局
/// - InitializeGpt (IOCTL_DISK_CREATE_DISK, GPT) — 初始化 GPT
/// - SetDriveLayout (IOCTL_DISK_SET_DRIVE_LAYOUT_EX) — 设置驱动器布局
/// - RefreshDisk (IOCTL_DISK_UPDATE_PROPERTIES) — 刷新磁盘属性
/// - ReenumerateDisk (CM_Locate_DevNodeW / CM_Reenumerate_DevNode) — PnP 重新枚举
/// </summary>
public sealed class DiskIOWriter
{
    private readonly ILoggerService _logger;

    public DiskIOWriter(ILoggerService logger)
    {
        _logger = logger;
    }

    // TODO: 实现磁盘写操作
}
