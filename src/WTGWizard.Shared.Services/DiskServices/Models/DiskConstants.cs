using System;

namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 磁盘相关常量 — GPT GUID（Guid / PowerShell 字符串）、分区布局、盘符回退链。
/// 磁盘物理布局参数的唯一来源（Main / DeploymentCore 均引用本类）。
/// </summary>
public static class DiskConstants
{
    // ═══ GPT 分区类型 GUID ═══

    public static readonly Guid EspGptType = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
    public static readonly Guid MsrGptType = new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");
    public static readonly Guid BasicDataGptType = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");

    public const string EspGptTypePS = "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}";
    public const string MsrGptTypePS = "{e3c9e316-0b5c-4db8-817d-f92df00215ae}";
    public const string BasicDataGptTypePS = "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}";

    // ═══ GPT 磁盘布局 ═══

    // reserved for DiskIOWriter PInvoke rewrite.
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
    // reserved for DiskIOWriter PInvoke rewrite.
    public const int EfiPartSizeMin = 300;
    public const int EfiPartSizeMax = 500;
}
