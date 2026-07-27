using System.Threading.Tasks;

namespace WTGWizard.Shared.Services.Disk;

/// <summary>
/// 盘符分配服务 — 两阶段模型：预留（脚本生成前）+ 实际查询（分区完成后）。
/// </summary>
public interface IDriveLetterService
{
    /// <summary>
    /// 阶段 1：全新安装模式下，从回退链中为 ESP 和 OS 各选一个未被占用的盘符。
    /// </summary>
    (char esp, char os) ReserveForCleanInstall();

    /// <summary>
    /// 阶段 1：分区安装模式下，仅为 ESP 选一个未被占用的盘符。
    /// </summary>
    char ReserveForPartitionInstall();

    /// <summary>
    /// 阶段 2：查询指定分区的实际盘符。
    /// </summary>
    Task<char> QueryActualDriveLetterAsync(uint diskNumber, uint partitionNumber, int maxRetries = 3);
}
