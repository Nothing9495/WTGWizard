using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WTGWizard.Shared.Services.Wim;

/// <summary>
/// WIM 服务接口 — 封装 ManagedWimLib，提供统一的 WIM 操作 API。
/// </summary>
public interface IWimService
{
    /// <summary>枚举映像中的所有索引。</summary>
    Task<IReadOnlyList<int>> EnumerateIndicesAsync(string imagePath, CancellationToken ct = default);

    /// <summary>获取映像元数据（包含应答文件检测）。</summary>
    Task<ImageInfo> GetImageInfo(string imagePath, int index, CancellationToken ct = default);

    /// <summary>校验映像完整性。失败时抛出异常。</summary>
    Task VerifyAsync(string imagePath, CancellationToken ct = default);

    /// <summary>提取映像内指定文件到目录。</summary>
    Task ExtractFileAsync(string imagePath, int index, string wimFilePath, string targetDir, CancellationToken ct = default);

    /// <summary>将 WIM 映像提取到目标目录。等价于 DISM /Apply-Image。</summary>
    Task ExtractImageAsync(
        string imagePath, int index, string targetDir,
        IProgress<(ulong current, ulong total)>? progress = null,
        CancellationToken ct = default);
}
