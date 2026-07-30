using System;

namespace WTGWizard.Main.DeploymentCore.Models;

/// <summary>
/// 部署专用常量 — GPT GUID 字符串、磁盘布局参数、超时值。
/// </summary>
public static class DeploymentConstants
{
    public const string EspGptTypePS = "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}";
    public const string MsrGptTypePS = "{e3c9e316-0b5c-4db8-817d-f92df00215ae}";
    public const string BasicDataGptTypePS = "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}";

    public const double BytesPerGiB = 1073741824.0;
    public const int MsrSizeMb = 16;

    public const uint CleanInstallEspPartNum = 1;
    public const uint CleanInstallOsPartNum = 3;

    public static readonly char[] EspFallbackChain = { 'Z', 'Y', 'W', 'V', 'U', 'T', 'S' };
    public static readonly char[] OsFallbackChain = { 'X', 'W', 'V', 'U', 'T', 'S', 'R' };
    public static readonly char[] ReservedFallbackChain = { 'R', 'Q', 'P', 'O', 'N', 'V', 'U', 'T', 'S' };

    public const int TimeoutPartitionMs = 300_000;
    public const int TimeoutExtractMs = 0;
    public const int TimeoutDriverMs = 600_000;
    public const int TimeoutApplyWtgMs = 120_000;
    public const int TimeoutBcdbootMs = 120_000;
    public const int TimeoutCleanupMs = 300_000;

    public const int ProgressMax = 100;
}
