using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ManagedWimLib;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Shared.Services.Wim;

/// <summary>
/// 统一 WIM 操作服务 — 封装 ManagedWimLib，统一初始化/错误处理/进度回调。
/// </summary>
public sealed class WimService : IWimService
{
    private readonly ILoggerService _logger;

    // ══════════════════════════════════════════════════════
    //  初始化
    // ══════════════════════════════════════════════════════

    private static readonly Lazy<bool> _initialized = new(
        valueFactory: () =>
        {
            var libPath = Path.Combine(AppContext.BaseDirectory, "Native\\x64\\libwim-15.dll");
            ManagedWimLib.Wim.GlobalInit(libPath);
            return true;
        },
        mode: LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureInitialized() => _ = _initialized.Value;

    public static void Cleanup()
    {
        if (_initialized.IsValueCreated)
            ManagedWimLib.Wim.TryGlobalCleanup();
    }

    public static bool IsInitialized => _initialized.IsValueCreated;

    public WimService(ILoggerService logger)
    {
        _logger = logger;
        EnsureInitialized();
        _logger.Debug("WimService", "WimService initialized");
    }

    // ══════════════════════════════════════════════════════
    //  映像信息
    // ══════════════════════════════════════════════════════

    /// <summary>应答文件检测路径（按优先级排列）。</summary>
    private static readonly string[] AnsFileSearchPaths =
    [
        @"\Windows\Panther\Unattend\unattend.xml",
        @"\Windows\Panther\Unattend\autounattend.xml",
        @"\Windows\Panther\unattend.xml",
        @"\Windows\System32\Sysprep\unattend.xml",
    ];

    /// <summary>枚举映像中的所有索引。</summary>
    public async Task<IReadOnlyList<int>> EnumerateIndicesAsync(string imagePath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var wim = ManagedWimLib.Wim.OpenWim(imagePath, OpenFlags.None);
                var count = (int)wim.GetWimInfo().ImageCount;
                IReadOnlyList<int> indices = Enumerable.Range(1, count).ToList();
                _logger.Debug("WimService", "Enumerate indices: {ImagePath}, count={Count}", imagePath, count);
                return indices;
            }
            catch (Exception ex)
            {
                _logger.Error("WimService", "OpenWim failed: {ImagePath} — {ErrorType}: {ErrorMessage}", 
                    imagePath, ex.GetType().Name, ex.Message);
                throw;
            }
        });
    }

    /// <summary>获取映像元数据（包含应答文件检测）。</summary>
    public async Task<ImageInfo> GetImageInfo(string imagePath, int index, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            _logger.Debug("WimService", "GetImageInfo: index={Index}", index);
            try
            {
                using var wim = ManagedWimLib.Wim.OpenWim(imagePath, OpenFlags.None);

                // 检测应答文件
                var foundPaths = new List<string>();
                foreach (var path in AnsFileSearchPaths)
                {
                    if (wim.FileExists(index, path))
                        foundPaths.Add(path);
                }

                var info = BuildImageInfo(wim, index, foundPaths);
                return info;
            }
            catch (Exception ex)
            {
                _logger.Error("WimService", "GetImageInfo failed (index {Index}): {ErrorMessage}", index, ex.Message);
                throw;
            }
        });
    }

    /// <summary>校验映像完整性。失败时抛出异常。</summary>
    public async Task VerifyAsync(string imagePath, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var wim = ManagedWimLib.Wim.OpenWim(imagePath, OpenFlags.None);
            wim.VerifyWim();
            _logger.Debug("WimService", "Verify passed: {ImagePath}", imagePath);
        });
    }

    /// <summary>提取映像内指定文件到目录。</summary>
    public async Task ExtractFileAsync(string imagePath, int index, string wimFilePath, string targetDir, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var wim = ManagedWimLib.Wim.OpenWim(imagePath, OpenFlags.None);
                Directory.CreateDirectory(targetDir);
                wim.ExtractPath(index, targetDir, wimFilePath, ExtractFlags.None);
                _logger.Debug("WimService", "Extract file: {WimFilePath} → {TargetDir}", wimFilePath, targetDir);
            }
            catch (Exception ex)
            {
                _logger.Error("WimService", "Extract file failed: {ErrorMessage}", ex.Message);
                throw;
            }
        });
    }

    // ══════════════════════════════════════════════════════
    //  提取映像 (核心功能 — 替代 DISM /Apply-Image)
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 将 WIM 映像提取到目标目录。等价于 DISM /Apply-Image。
    /// 支持进度回调和取消。
    /// </summary>
    public async Task ExtractImageAsync(
        string imagePath, int index, string targetDir,
        IProgress<(ulong current, ulong total)>? progress = null,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            try
            {
                using var wim = ManagedWimLib.Wim.OpenWim(imagePath, OpenFlags.None, OnProgress, null);
                _logger.Debug("WimService", "ExtractImage #{Index}: {ImagePath} → {TargetDir}", index, imagePath, targetDir);
                wim.ExtractImage(index, targetDir, ExtractFlags.None);
                _logger.Debug("WimService", "ExtractImage #{Index} completed", index);

                CallbackStatus OnProgress(ProgressMsg msg, object? info, object? ctx)
                {
                    // 通过返回 Abort 触发 wimlib 内部取消，而非抛异常穿过原生代码边界
                    if (ct.IsCancellationRequested)
                        return CallbackStatus.Abort;

                    if (info is ExtractProgress ep)
                    {
                        progress?.Report((ep.CompletedBytes, ep.TotalBytes));
                    }

                    return CallbackStatus.Continue;
                }
            }
            catch (WimLibException ex) when (ex.ErrorCode == ErrorCode.AbortedByProgress)
            {
                _logger.Warn("WimService", "ExtractImage cancelled");
                throw new OperationCanceledException(ct);
            }
            catch (Exception ex)
            {
                _logger.Error("WimService", "ExtractImage failed: {ErrorMessage}", ex.Message);
                throw;
            }
        }, ct);
    }

    // ══════════════════════════════════════════════════════
    //  辅助方法
    // ══════════════════════════════════════════════════════

    /// <summary>XML 元数据解析结果。</summary>
    private record XmlMetadata(
        string Sku, string Arch, string Major, string Minor, string Build,
        string? SpBuild, string? SpLevel, string ExpandedSize, string DateCreated, string DisplayDesc);

    /// <summary>从已打开的 WIM 对象解析映像元数据。</summary>
    private static ImageInfo BuildImageInfo(ManagedWimLib.Wim wim, int index, IReadOnlyList<string> ansFilePaths)
    {
        var name = wim.GetImageName(index) ?? string.Empty;
        var desc = wim.GetImageDescription(index) ?? string.Empty;

        var xml = ParseXmlMetadata(wim, index);

        var majorVer = TryParseInt(xml.Major) ?? 0;
        var featureVer = ResolveFeatureVer(xml.Major, xml.Minor, xml.Build);
        var arch = MapArch(xml.Arch);
        var buildStr = string.IsNullOrEmpty(xml.Build) ? "unknown" : $"{xml.Major}.{xml.Minor}.{xml.Build}";
        if (!string.IsNullOrEmpty(xml.SpBuild)) buildStr += $".{xml.SpBuild}";
        if (!string.IsNullOrEmpty(xml.SpLevel)) buildStr += $".{xml.SpLevel}";

        return new ImageInfo(
            Index: index, Name: name, Description: desc,
            DisplayDescription: xml.DisplayDesc,
            MajorVersion: majorVer, FeatureVersion: featureVer,
            Sku: xml.Sku, Architecture: arch, BuildNumber: buildStr,
            ExpandedSizeGB: double.TryParse(xml.ExpandedSize, out var sz) ? Math.Round(sz / WimConstants.BytesPerGiB, 2) : 0,
            DateCreated: xml.DateCreated,
            AnsFilePaths: ansFilePaths);
    }

    /// <summary>从 WIM XML 数据解析元数据。</summary>
    private static XmlMetadata ParseXmlMetadata(ManagedWimLib.Wim wim, int index)
    {
        var displayDesc = string.Empty;
        var sku = string.Empty;
        var archRaw = string.Empty;
        var majorStr = "0";
        var minorStr = "0";
        var build = string.Empty;
        var spBuild = (string?)null;
        var spLevel = (string?)null;
        var expandedSize = "0";
        var dateCreated = string.Empty;

        try
        {
            var xml = wim.GetXmlData();
            if (!string.IsNullOrEmpty(xml))
            {
                var cleanXml = xml.TrimStart('\uFEFF', '\uFFFE', ' ', '\t', '\r', '\n');
                var doc = XDocument.Parse(cleanXml);
                var root = doc.Root;
                if (root is not null)
                {
                    var imgNode = root.Elements("IMAGE")
                        .FirstOrDefault(e => (string?)e.Attribute("INDEX") == index.ToString())
                        ?? root.Element("IMAGE");

                    if (imgNode is not null)
                    {
                        displayDesc = imgNode.Element("DISPLAYDESCRIPTION")?.Value ?? string.Empty;

                        var win = imgNode.Element("WINDOWS");
                        if (win is not null)
                        {
                            sku = win.Element("SKU")?.Value
                               ?? win.Element("EDITIONID")?.Value
                               ?? string.Empty;
                            archRaw = win.Element("ARCH")?.Value ?? string.Empty;

                            var verEl = win.Element("VERSION");
                            if (verEl is not null)
                            {
                                majorStr = verEl.Element("MAJOR")?.Value ?? "0";
                                minorStr = verEl.Element("MINOR")?.Value ?? "0";
                                build = verEl.Element("BUILD")?.Value ?? string.Empty;
                                spBuild = verEl.Element("SPBUILD")?.Value;
                                spLevel = verEl.Element("SPLEVEL")?.Value;
                            }
                        }

                        expandedSize = imgNode.Element("TOTALBYTES")?.Value ?? "0";

                        var creation = imgNode.Element("CREATIONTIME");
                        if (creation is not null)
                        {
                            var high = creation.Element("HIGHPART")?.Value;
                            var low = creation.Element("LOWPART")?.Value;
                            dateCreated = ParseFileTime(high, low);
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // XML 解析失败，使用默认值
        }

        return new XmlMetadata(
            Sku: sku, Arch: archRaw, Major: majorStr, Minor: minorStr, Build: build,
            SpBuild: spBuild, SpLevel: spLevel, ExpandedSize: expandedSize,
            DateCreated: dateCreated, DisplayDesc: displayDesc);
    }

    private static int? TryParseInt(string? s) =>
        int.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static readonly Dictionary<string, string> ArchMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0"] = "x86", ["6"] = "IA64", ["9"] = "x64",
        ["5"] = "ARM", ["12"] = "ARM64",
    };

    private static string MapArch(string raw) =>
        ArchMap.TryGetValue(raw, out var name) ? name : raw;

    /// <summary>解析 wimlib CREATIONTIME 的 HIGHPART/LOWPART 十六进制 FILETIME。</summary>
    private static string ParseFileTime(string? high, string? low)
    {
        if (high is null || low is null) return string.Empty;
        try
        {
            var h = high.Replace("0x", "");
            var l = low.Replace("0x", "");
            if (long.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var hv) &&
                long.TryParse(l, System.Globalization.NumberStyles.HexNumber, null, out var lv))
            {
                var ft = ((long)hv << 32) | (uint)lv;
                return DateTime.FromFileTimeUtc(ft).ToLocalTime().ToString("yyyy-MM-dd");
            }
        }
        catch (Exception)
        {
            // 解析失败
        }
        return string.Empty;
    }

    private static readonly Dictionary<string, string> KnownBuilds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["22000"] = "21H2", ["22621"] = "22H2", ["22631"] = "23H2",
        ["26100"] = "24H2", ["26200"] = "25H2", ["26300"] = "26H2",
        ["28000"] = "26H1",
    };

    private static string ResolveFeatureVer(string? major, string? minor, string? build)
    {
        if (major is null || minor is null || build is null) return string.Empty;
        return KnownBuilds.TryGetValue(build, out var ver) ? ver : $"Build {build}";
    }
}
