using System;
using System.Linq;
using System.Text;
using WTGWizard.Main.DeploymentCore.Models;
using static WTGWizard.Shared.Services.DiskServices.DiskConstants;

namespace WTGWizard.Main.DeploymentCore.Builders;

/// <summary>
/// PowerShell Storage 模块脚本生成器 — 处理磁盘分区、格式化、清理。
/// </summary>
public static class DiskScriptBuilder
{
    public static string BuildCleanInstall(DeploymentConfig config)
    {
        var sb = new StringBuilder();
        int diskNum = config.DiskSelectedId;
        char espLetter = config.EspDriveLetter;
        char osLetter = config.OsDriveLetter;
        int efiSize = config.EfiPartSize;
        long osSizeBytes = (long)Math.Round(config.OsDriveSize * BytesPerGiB);
        string osLabel = FormatLabel(config.OsDriveLabel);
        string resLabel = FormatLabel(config.ReservedDriveLabel);
        string resFs = string.IsNullOrWhiteSpace(config.ReservedDriveFs) ? "ntfs" : config.ReservedDriveFs;
        string resFsUpper = resFs.ToUpperInvariant();

        WritePreamble(sb, diskNum, "Clean Install");

        sb.AppendLine("# Partition numbers");
        sb.AppendLine("$espPartNum = 0");
        sb.AppendLine("$osPartNum = 0");
        if (config.EnableReservedVol)
            sb.AppendLine("$reservedPartNum = 0");
        sb.AppendLine();

        WritePartitionPlanComment(sb, config, efiSize, osSizeBytes, osLabel, resLabel, resFsUpper);

        sb.AppendLine("Write-Host '=== Starting Disk Layout Creation ==='");
        WriteDiskInfo(sb, diskNum);
        WriteSafetyCheck(sb, diskNum);
        WriteReleaseDriveLetters(sb, diskNum);
        WriteClearDisk(sb, diskNum);
        WriteInitializeDisk(sb, diskNum);
        WriteRemoveAutoCreatedPartition(sb, diskNum);

        string espPartVar = WriteCreateAndFormatPartition(
            sb, diskNum, "ESP", efiSize, "MB", EspGptTypePS, "FAT32", "EFI_SYSTEM", espLetter);

        sb.AppendLine("Write-Host '--- Create MSR Partition ---'");
        sb.AppendLine($"New-Partition -DiskNumber $diskNum -Size {MsrSizeMb}MB -GptType '{MsrGptTypePS}' | Out-Null");
        sb.AppendLine("Write-Host '     MSR partition created.'");
        sb.AppendLine();

        long? osSize = config.EnableReservedVol ? osSizeBytes : null;
        string osPartVar = WriteCreateAndFormatPartition(
            sb, diskNum, "OS", osSize, null, BasicDataGptTypePS, "NTFS", osLabel, osLetter);

        if (config.NoDefaultDriveLetter)
        {
            sb.AppendLine($"Set-Partition -DiskNumber $diskNum -PartitionNumber ${osPartVar}PartNum -NoDefaultDriveLetter $true");
            sb.AppendLine("Write-Host '     NoDefaultDriveLetter property set on OS partition.'");
        }

        if (config.EnableReservedVol)
        {
            string resPartVar = WriteCreateAndFormatPartition(
                sb, diskNum, "Reserved", null, null, BasicDataGptTypePS, resFsUpper, resLabel, null, fatalFormat: false);
            WriteAssignReservedLetter(sb, diskNum, resPartVar);
        }

        WritePartitionSummary(sb, diskNum, config, osLabel, resLabel, resFsUpper);
        sb.AppendLine("Write-Host '=== Finishing Disk Layout Creation ==='");
        return sb.ToString();
    }

    public static string BuildPartitionInstall(DeploymentConfig config)
    {
        var sb = new StringBuilder();
        int diskNum = config.DiskSelectedId;
        char espLetter = config.EspDriveLetter;
        uint espPartNum = config.EspVolumeId;
        uint osPartNum = config.OsDriveVolumeId;
        string osLabel = FormatLabel(config.OsDriveLabel);

        WritePreamble(sb, diskNum, "Partition Install");

        sb.AppendLine("Write-Host '=== Starting Disk Layout Creation ==='");
        WriteDiskInfo(sb, diskNum);
        WriteSafetyCheck(sb, diskNum);

        WriteFormatPartition(sb, diskNum, espPartNum, "ESP", "FAT32", "EFI_SYSTEM", espLetter, removeLetter: true);
        WriteFormatPartition(sb, diskNum, osPartNum, "OS", "NTFS", osLabel, null, removeLetter: false);

        if (config.NoDefaultDriveLetter)
        {
            sb.AppendLine($"Set-Partition -DiskNumber $diskNum -PartitionNumber {osPartNum} -NoDefaultDriveLetter $true");
            sb.AppendLine("Write-Host '     NoDefaultDriveLetter property set on OS partition.'");
        }

        sb.AppendLine("Write-Host '=== Finishing Disk Layout Creation ==='");
        return sb.ToString();
    }

    public static string BuildCleanup(int diskNum, bool autoRemoveOs, uint espPartNum, uint osPartNum)
    {
        var sb = new StringBuilder();

        WritePreamble(sb, diskNum, "Post-deployment Cleanup", errorAction: "SilentlyContinue");

        sb.AppendLine("Write-Host '=== Post-deployment Cleanup ==='");
        sb.AppendLine();

        WriteRemoveDriveLetter(sb, diskNum, espPartNum, "ESP");

        if (autoRemoveOs)
            WriteRemoveDriveLetter(sb, diskNum, osPartNum, "OS");

        sb.AppendLine("Write-Host '=== Cleanup Complete ==='");
        return sb.ToString();
    }

    private static void WritePreamble(StringBuilder sb, int diskNum, string mode, string errorAction = "Stop")
    {
        sb.AppendLine($"# WTGWizard - {mode}");
        sb.AppendLine($"$ErrorActionPreference = '{errorAction}'");
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        sb.AppendLine("chcp 65001 > $null");
        sb.AppendLine("try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch {}");
        sb.AppendLine($"$diskNum = {diskNum}");
        sb.AppendLine();
    }

    private static void WriteDiskInfo(StringBuilder sb, int diskNum)
    {
        sb.AppendLine("Write-Host '--- Disk Info ---'");
        sb.AppendLine("$disk = Get-Disk -Number $diskNum -ErrorAction SilentlyContinue");
        sb.AppendLine("if (-not $disk) { throw \"ACTION FAILED: Disk #$diskNum not found\" }");
        sb.AppendLine("$wmiDisk = Get-CimInstance -ClassName Win32_DiskDrive -Filter \"Index = $diskNum\"");
        sb.AppendLine("if ($wmiDisk) {");
        sb.AppendLine("    Write-Host (\"     Model:     \" + $wmiDisk.Model)");
        sb.AppendLine("    Write-Host (\"     Interface: \" + $disk.BusType + \" | Media: \" + $wmiDisk.MediaType)");
        sb.AppendLine("} else {");
        sb.AppendLine("    Write-Host (\"     Model:     \" + $disk.FriendlyName)");
        sb.AppendLine("    Write-Host (\"     Interface: \" + $disk.BusType)");
        sb.AppendLine("}");
        sb.AppendLine("Write-Host (\"     Size:      \" + [math]::Round($disk.Size / 1GB, 2) + \" GB | Style: \" + $disk.PartitionStyle + \" | Status: \" + $disk.OperationalStatus)");
        sb.AppendLine();
    }

    private static void WriteSafetyCheck(StringBuilder sb, int diskNum)
    {
        sb.AppendLine("Write-Host '--- Safety Check ---'");
        sb.AppendLine("$internalBuses = @('NVMe', 'SATA', 'SAS', 'SCSI', 'RAID', 'iSCSI', 'ATA', 'IDE')");
        sb.AppendLine("if ($disk.BusType -in $internalBuses) {");
        sb.AppendLine($"    throw (\"ACTION FAILED: INTERNAL Disk #$diskNum detected, operation aborted! (Bus Type: \" + $disk.BusType + \")\" )");
        sb.AppendLine("}");
        sb.AppendLine("$osDrive = (Get-CimInstance Win32_OperatingSystem).SystemDrive.TrimEnd(':')");
        sb.AppendLine("$osPartition = Get-Partition -DriveLetter $osDrive -ErrorAction SilentlyContinue");
        sb.AppendLine("if ($osPartition -and $osPartition.DiskNumber -eq $diskNum) {");
        sb.AppendLine($"    throw (\"ACTION FAILED: SYSTEM volume detected on Disk #$diskNum, operation aborted! (Drive: \" + $osDrive + \":)\" )");
        sb.AppendLine("}");
        sb.AppendLine("$targetVolumes = Get-Partition -DiskNumber $diskNum -ErrorAction SilentlyContinue |");
        sb.AppendLine("    Where-Object { $_.DriveLetter } | Select-Object -ExpandProperty DriveLetter");
        sb.AppendLine("foreach ($dl in $targetVolumes) {");
        sb.AppendLine("    $pageFile = Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue |");
        sb.AppendLine("        Where-Object { $_.Name.Substring(0, 1) -eq $dl }");
        sb.AppendLine($"    if ($pageFile) {{ throw (\"ACTION FAILED: Active page file detected on volume \" + $dl + \": on Disk #$diskNum, operation aborted!\") }}");
        sb.AppendLine("}");
        sb.AppendLine("Write-Host '     Environment check passed. Proceeding with disk operations...'");
        sb.AppendLine();
    }

    private static void WriteReleaseDriveLetters(StringBuilder sb, int diskNum)
    {
        sb.AppendLine("Write-Host '--- Release Drive Letters ---'");
        sb.AppendLine("Get-Partition -DiskNumber $diskNum -ErrorAction SilentlyContinue | ForEach-Object {");
        sb.AppendLine("    $p = $_");
        sb.AppendLine("    $letterPath = $p.AccessPaths | Where-Object { $_ -match '^[A-Z]:' } | Select -First 1");
        sb.AppendLine("    if ($letterPath) {");
        sb.AppendLine("        try {");
        sb.AppendLine("            $p | Remove-PartitionAccessPath -AccessPath $letterPath -ErrorAction Stop | Out-Null");
        sb.AppendLine($"            Write-Host (\"     Released drive letter \" + $p.DriveLetter + \": from partition #\" + $p.PartitionNumber + \" on disk #{diskNum}\")");
        sb.AppendLine("        } catch {");
        sb.AppendLine($"            Write-Warning (\"WARNING: Failed to release drive letter \" + $p.DriveLetter + \": from partition #\" + $p.PartitionNumber + \" on disk #{diskNum} — \" + $_.Exception.Message)");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteClearDisk(StringBuilder sb, int diskNum)
    {
        sb.AppendLine("Write-Host '--- Clear Disk ---'");
        sb.AppendLine("try {");
        sb.AppendLine("    Clear-Disk -Number $diskNum -RemoveData -RemoveOEM -Confirm:$false -ErrorAction Stop");
        sb.AppendLine($"    Write-Host '     Disk #{diskNum} cleared successfully.'");
        sb.AppendLine("} catch {");
        sb.AppendLine($"    Write-Warning (\"WARNING: Clear-Disk failed on disk #{diskNum} — \" + $_.Exception.Message)");
        sb.AppendLine("    Get-Partition -DiskNumber $diskNum -ErrorAction SilentlyContinue |");
        sb.AppendLine("        Where-Object { $_.PartitionNumber -gt 0 } |");
        sb.AppendLine("        Remove-Partition -Confirm:$false -ErrorAction SilentlyContinue | Out-Null");
        sb.AppendLine("    Write-Host '     Partitions removed individually.'");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteInitializeDisk(StringBuilder sb, int diskNum)
    {
        sb.AppendLine("Write-Host '--- Initialize Disk ---'");
        sb.AppendLine("$disk = Get-Disk -Number $diskNum");
        sb.AppendLine("if ($disk.PartitionStyle -eq 'RAW') {");
        sb.AppendLine("    Initialize-Disk -Number $diskNum -PartitionStyle GPT -Confirm:$false | Out-Null");
        sb.AppendLine("    Write-Host '     Disk initialized as GPT.'");
        sb.AppendLine("} else {");
        sb.AppendLine("    Write-Host (\"     Disk initialization skipped (current: \" + $disk.PartitionStyle + \").\")");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteRemoveAutoCreatedPartition(StringBuilder sb, int diskNum)
    {
        sb.AppendLine("Write-Host '--- Remove Auto-created Partition ---'");
        sb.AppendLine("$p1 = Get-Partition -DiskNumber $diskNum -PartitionNumber 1 -ErrorAction SilentlyContinue");
        sb.AppendLine("if ($p1) {");
        sb.AppendLine("    $p1 | Remove-Partition -Confirm:$false -ErrorAction SilentlyContinue | Out-Null");
        sb.AppendLine("    Write-Host '     Auto-created partition 1 removed.'");
        sb.AppendLine("} else {");
        sb.AppendLine("    Write-Host '     No auto-created partition found.'");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string WriteCreateAndFormatPartition(
        StringBuilder sb, int diskNum, string name,
        long? sizeBytes, string? sizeSuffix, string gptType, string fs, string label, char? driveLetter,
        bool fatalFormat = true)
    {
        string varName = name.ToLowerInvariant();
        string sizeArg = sizeBytes is null ? "-UseMaximumSize" : $"-Size {sizeBytes}{sizeSuffix}";

        sb.AppendLine($"Write-Host '--- Create {name} Partition ---'");
        sb.AppendLine($"${varName} = New-Partition -DiskNumber $diskNum {sizeArg} -GptType '{gptType}'");
        sb.AppendLine($"${varName}PartNum = ${varName}.PartitionNumber");
        sb.AppendLine($"Write-Host (\"     {name} partition = #\" + ${varName}PartNum)");

        if (fatalFormat)
        {
            sb.AppendLine($"Format-Volume -Partition ${varName} -FileSystem {fs} -NewFileSystemLabel '{label}' -Confirm:$false -ErrorAction Stop | Out-Null");
            sb.AppendLine($"Write-Host '     {name} partition formatted.'");
        }
        else
        {
            sb.AppendLine("try {");
            sb.AppendLine($"    Format-Volume -Partition ${varName} -FileSystem {fs} -NewFileSystemLabel '{label}' -Confirm:$false -ErrorAction Stop | Out-Null");
            sb.AppendLine($"    Write-Host '     {name} partition formatted.'");
            sb.AppendLine("} catch {");
            sb.AppendLine($"    Write-Warning (\"WARNING: Failed to format {name} partition #\" + ${varName}PartNum + \" on disk #{diskNum} — \" + $_.Exception.Message)");
            sb.AppendLine("}");
        }

        if (driveLetter is char letter)
        {
            sb.AppendLine($"Set-Partition -DiskNumber $diskNum -PartitionNumber ${varName}PartNum -NewDriveLetter '{letter}' -ErrorAction Stop | Out-Null");
            sb.AppendLine($"Write-Host \"     {name} drive letter: {letter}\"");
        }

        sb.AppendLine();
        return varName;
    }

    private static void WriteFormatPartition(
        StringBuilder sb, int diskNum, uint partNum, string name, string fs, string label, char? driveLetter,
        bool removeLetter = true)
    {
        sb.AppendLine($"Write-Host '--- Format {name} Partition ---'");
        sb.AppendLine($"$part = Get-Partition -DiskNumber $diskNum -PartitionNumber {partNum} -ErrorAction Stop");
        sb.AppendLine("$currentLetter = if ($part.DriveLetter) { $part.DriveLetter.ToString() } else { '(none)' }");
        sb.AppendLine("Write-Host (\"     Current drive letter: \" + $currentLetter)");

        if (removeLetter)
        {
            sb.AppendLine("$letterPath = $part.AccessPaths | Where-Object { $_ -match '^[A-Z]:' } | Select -First 1");
            sb.AppendLine("if ($letterPath) {");
            sb.AppendLine("    try {");
            sb.AppendLine("        $part | Remove-PartitionAccessPath -AccessPath $letterPath -ErrorAction Stop | Out-Null");
            sb.AppendLine($"        Write-Host (\"     Removed drive letter from {name} partition #{partNum}.\")");
            sb.AppendLine("    } catch {");
            sb.AppendLine($"        Write-Warning (\"WARNING: Failed to remove drive letter from {name} partition #{partNum} on disk #{diskNum} — \" + $_.Exception.Message)");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }

        sb.AppendLine($"Format-Volume -Partition $part -FileSystem {fs} -NewFileSystemLabel '{label}' -Confirm:$false -ErrorAction Stop | Out-Null");
        sb.AppendLine($"Write-Host '     {name} partition formatted.'");

        if (driveLetter is char letter)
        {
            sb.AppendLine($"Set-Partition -DiskNumber $diskNum -PartitionNumber {partNum} -NewDriveLetter '{letter}' -ErrorAction Stop | Out-Null");
            sb.AppendLine($"Write-Host \"     {name} drive letter: {letter}\"");
        }

        sb.AppendLine();
    }

    private static void WriteRemoveDriveLetter(StringBuilder sb, int diskNum, uint partNum, string name)
    {
        sb.AppendLine($"Write-Host '--- Remove {name} Drive Letter ---'");
        sb.AppendLine($"$part = Get-Partition -DiskNumber $diskNum -PartitionNumber {partNum} -ErrorAction SilentlyContinue");
        sb.AppendLine("if ($part -and $part.DriveLetter) {");
        sb.AppendLine("    $accessPath = $part.DriveLetter.ToString() + ':'");
        sb.AppendLine("    try {");
        sb.AppendLine($"        Remove-PartitionAccessPath -DiskNumber $diskNum -PartitionNumber {partNum} -AccessPath $accessPath -ErrorAction Stop");
        sb.AppendLine($"        Write-Host (\"     {name} partition #{partNum} drive letter \" + $part.DriveLetter + \": removed from disk #{diskNum}\")");
        sb.AppendLine("    } catch {");
        sb.AppendLine($"        throw (\"ACTION FAILED: Failed to remove drive letter \" + $part.DriveLetter + \": from {name} partition #{partNum} on disk #{diskNum} — \" + $_.Exception.Message)");
        sb.AppendLine("    }");
        sb.AppendLine("} else {");
        sb.AppendLine($"    Write-Host (\"     {name} partition #{partNum} has no drive letter, skipped\")");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteAssignReservedLetter(StringBuilder sb, int diskNum, string partVar)
    {
        string letterList = string.Join(",", ReservedFallbackChain.Select(c => $"'{c}'"));

        sb.AppendLine("# Assign next available drive letter to Reserved partition");
        sb.AppendLine("$usedLetters = Get-Partition -ErrorAction SilentlyContinue |");
        sb.AppendLine("    Where-Object { $_.DriveLetter } | Select-Object -ExpandProperty DriveLetter");
        sb.AppendLine($"$availLetter = [char[]]@({letterList}) | Where-Object {{ $_ -notin $usedLetters }} | Select -First 1");
        sb.AppendLine("if ($availLetter) {");
        sb.AppendLine($"    Set-Partition -DiskNumber $diskNum -PartitionNumber ${partVar}PartNum -NewDriveLetter $availLetter -ErrorAction Stop | Out-Null");
        sb.AppendLine($"    Write-Host (\"     Reserved partition drive letter: \" + $availLetter + \": on disk #{diskNum}\")");
        sb.AppendLine("} else {");
        sb.AppendLine($"    Write-Warning (\"WARNING: No available drive letter for Reserved partition on disk #{diskNum}\")");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WritePartitionSummary(
        StringBuilder sb, int diskNum, DeploymentConfig config, string osLabel, string resLabel, string resFsUpper)
    {
        sb.AppendLine("# ═══════════════════════════════════════════");
        sb.AppendLine("#  Partition Summary:");
        sb.AppendLine("Write-Host ''");
        sb.AppendLine("Write-Host '--- Partition Summary ---'");
        sb.AppendLine("$espDrive = (Get-Partition -DiskNumber $diskNum -PartitionNumber $espPartNum -ErrorAction SilentlyContinue).DriveLetter");
        sb.AppendLine("if (-not $espDrive) { $espDrive = '?' }");
        sb.AppendLine("Write-Host (\"     ESP:       #\" + $espPartNum + \"  FAT32  EFI_SYSTEM  Drive=\" + $espDrive + \":\")");

        string osSizeDisplay = config.EnableReservedVol ? $"{config.OsDriveSize:F2} GiB" : "Remaining";
        sb.AppendLine($"Write-Host \"     OS:        #$osPartNum  NTFS   {osLabel}  Drive={config.OsDriveLetter}:  Size={osSizeDisplay}\"");

        if (config.EnableReservedVol)
        {
            sb.AppendLine("$resDrive = (Get-Partition -DiskNumber $diskNum -PartitionNumber $reservedPartNum -ErrorAction SilentlyContinue).DriveLetter");
            sb.AppendLine("if (-not $resDrive) { $resDrive = '?' }");
            sb.AppendLine($"Write-Host (\"     Reserved:  #\" + $reservedPartNum + \"  {resFsUpper}  {resLabel}  Drive=\" + $resDrive + \":\")");
        }

        sb.AppendLine("Write-Host ''");
        sb.AppendLine("# ═══════════════════════════════════════════");
    }

    private static void WritePartitionPlanComment(
        StringBuilder sb, DeploymentConfig config, int efiSize, long osSizeBytes, string osLabel, string resLabel, string resFsUpper)
    {
        char espLetter = config.EspDriveLetter;
        char osLetter = config.OsDriveLetter;
        string osSizeDisplay = config.EnableReservedVol ? $"{config.OsDriveSize:F2} GiB" : "Remaining";

        sb.AppendLine("# ═══════════════════════════════════════════");
        sb.AppendLine("#  Partition Plan:");
        sb.AppendLine($"#    1. ESP: {efiSize}MB FAT32 label=EFI_SYSTEM -> {espLetter}:");
        sb.AppendLine($"#    2. MSR: {MsrSizeMb}MB (Microsoft Reserved)");
        sb.AppendLine($"#    3. OS:  {osSizeDisplay} NTFS label={osLabel} -> {osLetter}:");

        if (config.EnableReservedVol)
            sb.AppendLine($"#    4. Reserved: Remaining space {resFsUpper} label={resLabel} -> Auto-assign");

        sb.AppendLine("# ═══════════════════════════════════════════");
        sb.AppendLine();
    }

    private static string FormatLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "OS";
        return label.Replace("'", "''");
    }
}
