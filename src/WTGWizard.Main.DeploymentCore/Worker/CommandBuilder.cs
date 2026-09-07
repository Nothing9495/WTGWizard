using System;
using System.Diagnostics;
using System.IO;

namespace WTGWizard.Main.DeploymentCore.Worker;

/// <summary>
/// 外部工具命令参数构建器 — DISM / BCDBoot。
/// </summary>
public static class CommandBuilder
{
    public static string BuildApplyImageArgs(string imagePath, int index, string applyDir)
    {
        ValidateApplyDir(applyDir);
        return $"/Apply-Image /ImageFile:\"{imagePath}\" /Index:{index} /ApplyDir:{applyDir} /CheckIntegrity /Verify";
    }

    public static string? BuildAddDriverArgs(string applyDir, string? driverPath, bool forceUnsigned)
    {
        if (string.IsNullOrWhiteSpace(driverPath))
            return null;

        ValidateApplyDir(applyDir);

        string args = $"/Image:{applyDir} /Add-Driver /Driver:\"{driverPath}\" /Recurse";
        if (forceUnsigned)
            args += " /ForceUnsigned";
        return args;
    }

    public static string BuildApplyUnattendArgs(string applyDir, string filePath)
    {
        ValidateApplyDir(applyDir);
        return $"/Image:{applyDir} /Apply-Unattend:\"{filePath}\"";
    }

    /// <summary>
    /// 检测宿主 bcdboot.exe 是否支持 /offline（版本 ≥ 10.0.26100.0）。
    /// 与 Rufus 逻辑一致：检测工具自身版本而非宿主机 OS 版本。
    /// THANKS TO Microsoft, I CANNOT pinpoint exactly when or in which update they introduced the `/offline` switch to bcdboot.
    /// So I had to follow Rufus's approach instead.
    /// Microsoft's documents are a kind of mess, especially the Chinese version.
    /// </summary>
    public static bool SupportsOffline()
    {
        var bcdbootPath = Path.Combine(Environment.SystemDirectory, "bcdboot.exe");
        var fileVersion = FileVersionInfo.GetVersionInfo(bcdbootPath);
        return fileVersion.FileMajorPart == 10 && fileVersion.FileMinorPart == 0 && fileVersion.FileBuildPart >= 26100;
    }


    public static string BuildBcdbootArgs(string applyDir, char espDriveLetter,
        bool enableBootEx, bool enableBootVerbose, bool enableOffline)
    {
        ValidateApplyDir(applyDir);

        string args = enableOffline
            ? $"{applyDir}Windows /s {espDriveLetter}: /f UEFI /offline"
            : $"{applyDir}Windows /s {espDriveLetter}: /f UEFI";

        if (enableBootEx)
            args += " /bootex";

        if (enableBootVerbose)
            args += " /v";

        return args;
    }

    private static void ValidateApplyDir(string applyDir)
    {
        if (string.IsNullOrWhiteSpace(applyDir))
            throw new ArgumentException("applyDir cannot be null or empty", nameof(applyDir));
    }
}
