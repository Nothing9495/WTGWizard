using System;

namespace WTGWizard.Main.DeploymentCore.WorkerCore;

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

    public static string BuildBcdbootArgs(string applyDir, char espDriveLetter,
        bool enableBootEx, bool enableBootVerbose)
    {
        ValidateApplyDir(applyDir);

        string args = $"{applyDir}Windows /s {espDriveLetter}: /f UEFI /offline";

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
