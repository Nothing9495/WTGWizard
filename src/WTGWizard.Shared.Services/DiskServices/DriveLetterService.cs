using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 盘符分配服务实现。
/// </summary>
public sealed class DriveLetterService : IDriveLetterService
{
    private readonly IDiskIOService _diskService;
    private readonly ILoggerService _logger;

    public DriveLetterService(IDiskIOService diskService, ILoggerService logger)
    {
        _diskService = diskService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public (char esp, char os) ReserveForCleanInstall()
    {
        var used = GetUsedDriveLetters();

        var esp = PickFirstAvailable(DiskConstants.EspFallbackChain, used);
        used.Add(esp);

        var os = PickFirstAvailable(DiskConstants.OsFallbackChain, used);
        _logger.Debug("DriveLetter", "Reserved for clean install: ESP={Esp}, OS={Os}", esp, os);
        return (esp, os);
    }

    /// <inheritdoc/>
    public char ReserveForPartitionInstall()
    {
        var used = GetUsedDriveLetters();
        var esp = PickFirstAvailable(DiskConstants.EspFallbackChain, used);
        _logger.Debug("DriveLetter", "Reserved for partition install: ESP={Esp}", esp);
        return esp;
    }

    /// <inheritdoc/>
    public async Task<char> QueryActualDriveLetterAsync(uint diskNumber, uint partitionNumber, int maxRetries = 3)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            var partitions = await _diskService.GetPartitionsAsync(diskNumber, skipEsp: false);
            var part = partitions.FirstOrDefault(p => p.PartitionNumber == partitionNumber);

            if (part?.DriveLetter is { Length: 1 } letter)
                return letter[0];

            if (i < maxRetries - 1)
            {
                _logger.Warn("DriveLetter", "Drive letter retry: partition={Partition}, attempt={Attempt}/{MaxRetries}",
                    partitionNumber, i + 1, maxRetries);
                await Task.Delay(500);
            }
        }

        _logger.Error("DriveLetter", "Drive letter not found: partition={Partition}, disk={Disk}",
            partitionNumber, diskNumber);
        throw new InvalidOperationException(
            $"Drive letter not assigned for partition {partitionNumber} on disk {diskNumber} after {maxRetries} retries.");
    }

    /// <summary>从回退链中找到第一个不在 used 中的字母。</summary>
    private static char PickFirstAvailable(char[] chain, HashSet<char> used)
    {
        foreach (var c in chain)
        {
            if (!used.Contains(c))
                return c;
        }

        throw new InvalidOperationException(
            $"All drive letters occupied: {string.Join(", ", chain)}");
    }

    /// <summary>查询系统当前已挂载的所有盘符。</summary>
    private static HashSet<char> GetUsedDriveLetters()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType != DriveType.NoRootDirectory
                         && d.Name is { Length: >= 1 }
                         && char.IsLetter(d.Name[0]))
                .Select(d => char.ToUpperInvariant(d.Name[0]))
                .ToHashSet();
        }
        catch
        {
            return new HashSet<char>();
        }
    }
}
