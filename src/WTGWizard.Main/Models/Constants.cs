using System;

namespace WTGWizard.Models;

/// <summary>
/// 全局常量 — GPT GUID、分区布局、盘符回退链、磁盘计算参数。
/// </summary>
public static class Constants
{
    // ═══ GPT 分区类型 GUID ═══

    public static readonly Guid EspGptType = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
    public static readonly Guid MsrGptType = new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");
    public static readonly Guid BasicDataGptType = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");

    public const string EspGptTypePS = "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}";
    public const string MsrGptTypePS = "{e3c9e316-0b5c-4db8-817d-f92df00215ae}";
    public const string BasicDataGptTypePS = "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}";

    // ═══ GPT 磁盘布局 ═══

    public const long GptBackupSectors = 33;
    public const long AlignmentBytes = 1 * 1024 * 1024;
    public const double BytesPerGiB = 1073741824.0;
    public const int MsrSizeMb = 16;

    // ═══ Clean Install 固定分区号 ═══

    public const uint CleanInstallEspPartNum = 1;
    public const uint CleanInstallOsPartNum = 3;

    // ═══ 盘符回退链 ═══

    public static readonly char[] EspFallbackChain = { 'Z', 'Y', 'W', 'V', 'U', 'T', 'S' };
    public static readonly char[] OsFallbackChain = { 'X', 'W', 'V', 'U', 'T', 'S', 'R' };
    public static readonly char[] ReservedFallbackChain = { 'R', 'Q', 'P', 'O', 'N', 'V', 'U', 'T', 'S' };

    // ═══ EFI 分区大小范围 ═══

    public const int EfiPartSizeMin = 300;
    public const int EfiPartSizeMax = 500;

    // ═══ Windows 构建号阈值 ═══

    public const int BuildMajor26100 = 26100;
    public const int BuildMajor26200 = 26200;
    public const int BuildRevisionThreshold = 8037;

    // ═══ Worker 超时（毫秒） ═══

    public const int TimeoutPartitionMs = 300_000;   // 5 分钟（pwsh）
    public const int TimeoutExtractMs = 0;           // 无超时（WIM 提取耗时不可预测）
    public const int TimeoutDriverMs = 600_000;      // 10 分钟（DISM /Add-Driver）
    public const int TimeoutApplyWtgMs = 120_000;    // 2 分钟（DISM /Apply-Unattend）
    public const int TimeoutBcdbootMs = 120_000;     // 2 分钟（bcdboot）
    public const int TimeoutCleanupMs = 300_000;     // 5 分钟（pwsh）

    // ═══ 错误格式化 ═══

    public const int MaxErrorLines = 5;
    public const int MaxTailLines = 3;
    public const int ProgressMax = 100;
}
