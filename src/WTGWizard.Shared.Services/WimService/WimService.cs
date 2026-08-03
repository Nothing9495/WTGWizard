using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ManagedWimLib;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Shared.Services.WimService;

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
        _logger.Debug("WimService", "WimService: Initialized");
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
            _logger.Debug("WimService", "EnumerateIndicesAsync: imagePath={ImagePath}", imagePath);
            try
            {
                using var wim = ManagedWimLib.Wim.OpenWim(imagePath, OpenFlags.None);
                var count = (int)wim.GetWimInfo().ImageCount;
                IReadOnlyList<int> indices = Enumerable.Range(1, count).ToList();
                _logger.Info("WimService", "EnumerateIndicesAsync: {Count} indices found.", count);
                return indices;
            }
            catch (Exception ex)
            {
                _logger.Error("WimService", "EnumerateIndicesAsync: Method failed - ({ErrorType}: {Error}).", ex.GetType().Name, ex.Message);
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
            _logger.Debug("WimService", "GetImageInfo: Reading info for index {Index}", index);
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
                _logger.Debug("WimService", "GetImageInfo: index={Index}, name={Name}, arch={Arch}, build={Build}, expand={Expand}GB",
                    info.Index, info.Name, info.Architecture, info.BuildNumber, info.ExpandedSizeGB);
                if (foundPaths.Count > 0)
                    _logger.Debug("WimService", "GetImageInfo: AnsFile detected: {Paths}", string.Join(", ", foundPaths));
                _logger.Debug("WimService", "GetImageInfo: Loaded index {Index} info.", index);
                return info;
            }
            catch (Exception ex)
            {
                _logger.Error("WimService", "GetImageInfo: Method failed - ({Error}).", ex.Message);
                throw;
            }
        });
    }

    /// <summary>校验映像完整性。失败时抛出异常。</summary>
    public async Task VerifyAsync(string imagePath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            _logger.Info("WimService", "VerifyAsync: Verifying {ImagePath}", imagePath);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // ① 打开阶段 — 失败原样重抛（WimLibException → 调用方归 Failed）
                using var wim = ManagedWimLib.Wim.OpenWim(imagePath, OpenFlags.None, OnProgress, null);

                // ② 校验阶段 — 失败包装为 WimVerificationException（内容损坏 → NotPass）
                // 排除 AbortedByProgress：取消需穿透到外层转为 OperationCanceledException
                try { wim.VerifyWim(); }
                catch (WimLibException ex) when (ex.ErrorCode != ErrorCode.AbortedByProgress)
                {
                    _logger.Warn("WimService", "VerifyAsync: Image {ImagePath} failed verification - ({Error}).", imagePath, ex.Message);
                    // 用 GetErrorString（纯错误码描述，避免 wimlib 全局错误文件残留）；完整上下文已在日志
                    throw new WimVerificationException(Wim.GetErrorString(ex.ErrorCode), ex);
                }

                _logger.Info("WimService", "VerifyAsync: Image {ImagePath} passed verification in {Elapsed:F1}s.", imagePath, sw.Elapsed.TotalSeconds);

                CallbackStatus OnProgress(ProgressMsg msg, object? info, object? ctx)
                {
                    // 通过返回 Abort 触发 wimlib 内部取消，而非抛异常穿过原生代码边界
                    if (ct.IsCancellationRequested)
                        return CallbackStatus.Abort;

                    // 进度仅来自 VERIFY_STREAMS（数据流校验期）——
                    // BEGIN/END_VERIFY_IMAGE 仅标记各映像元数据校验的起止
                    if (msg == ProgressMsg.VerifyStreams && info is VerifyStreamsProgress vp
                        && vp.TotalBytes > 0)
                    {
                        progress?.Report(vp.CurrentBytes * 100.0 / vp.TotalBytes);
                    }

                    return CallbackStatus.Continue;
                }
            }
            catch (WimLibException ex) when (ex.ErrorCode == ErrorCode.AbortedByProgress)
            {
                _logger.Info("WimService", "VerifyAsync: Verification is aborted by progress.");
                throw new OperationCanceledException(ct);
            }
            // NotPass（内容损坏）已由内层 Warn 记录，避免在此重复记录为 Error
            catch (Exception ex) when (ex is not WimVerificationException)
            {
                _logger.Error("WimService", "VerifyAsync: Method failed - ({Error}).", ex.Message);
                throw;
            }
        }, ct);
    }

    /// <summary>提取映像内指定文件到指定目标文件路径（不保留目录结构、不提取 ACL）。</summary>
    public async Task ExtractFileAsync(string imagePath, int index, string wimFilePath, string targetFilePath, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            string? targetDir = Path.GetDirectoryName(targetFilePath);
            if (string.IsNullOrEmpty(targetDir))
                throw new ArgumentException("targetFilePath must have a directory", nameof(targetFilePath));
            Directory.CreateDirectory(targetDir);

            _logger.Debug("WimService", "ExtractFileAsync: Extracting {WimFilePath} (index {Index}) from {ImagePath} to {TargetFilePath}", wimFilePath, index, imagePath, targetFilePath);
            try
            {
                using var wim = ManagedWimLib.Wim.OpenWim(imagePath, OpenFlags.None);
                // 提取到目标目录（不保留目录结构 → 仅文件名；NoAcls → 不带 WIM 安全描述符）
                wim.ExtractPath(index, targetDir, wimFilePath,
                    ExtractFlags.NoPreserveDirStructure | ExtractFlags.NoAcls);

                // 移动为指定的目标文件名（覆盖已存在文件）
                string extracted = Path.Combine(targetDir, Path.GetFileName(wimFilePath));
                File.Move(extracted, targetFilePath, overwrite: true);

                _logger.Info("WimService", "ExtractFileAsync: Extracted {WimFilePath} to {TargetFilePath}", wimFilePath, targetFilePath);
            }
            catch (Exception ex)
            {
                _logger.Error("WimService", "ExtractFileAsync: Method failed - ({Error}).", ex.Message);
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
    /// <param name="imagePath">WIM 文件路径。</param>
    /// <param name="index">映像索引（1-based）。</param>
    /// <param name="targetDir">目标目录。</param>
    /// <param name="progress">进度回调（当前/总字节）。</param>
    /// <param name="stageChanged">阶段事件回调（由调用方决定如何展示）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task ExtractImageAsync(
        string imagePath, int index, string targetDir,
        IProgress<(ulong current, ulong total)>? progress = null,
        Action<WimExtractStage>? stageChanged = null,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            try
            {
                using var wim = ManagedWimLib.Wim.OpenWim(imagePath, OpenFlags.None, OnProgress, null);
                _logger.Info("WimService", "ExtractImage:  - Extracting index {Index} from {ImagePath} to {TargetDir}", index, imagePath, targetDir);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                wim.ExtractImage(index, targetDir, ExtractFlags.None);
                _logger.Info("WimService", "ExtractImage: {ImagePath} - Index {Index} extraction completed in {Elapsed:F1}s", imagePath, index, sw.Elapsed.TotalSeconds);

                CallbackStatus OnProgress(ProgressMsg msg, object? info, object? ctx)
                {
                    // 通过返回 Abort 触发 wimlib 内部取消，而非抛异常穿过原生代码边界
                    if (ct.IsCancellationRequested)
                        return CallbackStatus.Abort;

                    if (stageChanged is not null)
                    {
                        stageChanged(msg switch
                        {
                            ProgressMsg.ExtractImageBegin => WimExtractStage.ExtractImageBegin,
                            ProgressMsg.ExtractTreeBegin => WimExtractStage.ExtractTreeBegin,
                            ProgressMsg.ExtractFileStructure => WimExtractStage.ExtractFileStructure,
                            ProgressMsg.ExtractStreams => WimExtractStage.ExtractStreams,
                            ProgressMsg.ExtractMetadata => WimExtractStage.ExtractMetadata,
                            _ => WimExtractStage.ExtractStreams,
                        });
                    }

                    // 进度仅来自 EXTRACT_STREAMS（数据流期）——
                    // 阶段消息（IMAGE_BEGIN/FILE_STRUCTURE/METADATA）也携带
                    // ExtractProgress 字段，但那是阶段时点值（0%/100%），非流进度
                    if (msg == ProgressMsg.ExtractStreams && info is ExtractProgress ep)
                    {
                        progress?.Report((ep.CompletedBytes, ep.TotalBytes));
                    }

                    return CallbackStatus.Continue;
                }
            }
            catch (WimLibException ex) when (ex.ErrorCode == ErrorCode.AbortedByProgress)
            {
                _logger.Info("WimService", "ExtractImage: Extraction is aborted by progress.");
                throw new OperationCanceledException(ct);
            }
            catch (Exception ex)
            {
                _logger.Error("WimService", "ExtractImage: Method failed - ({Error}).", ex.Message);
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
        // Windows 10 Build
        ["19041"] = "2004", ["19042"] = "20H2", ["19043"] = "21H1",
        ["19044"] = "21H2", ["19045"] = "22H2",
        // Windows 11 Build
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
