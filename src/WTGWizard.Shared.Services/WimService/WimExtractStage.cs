namespace WTGWizard.Shared.Services.WimService;

/// <summary>
/// WIM 提取阶段枚举 — 结构化阶段事件，供调用方（Worker）映射为人类可读消息。
/// </summary>
public enum WimExtractStage
{
    ExtractImageBegin,
    ExtractTreeBegin,
    ExtractFileStructure,
    ExtractStreams,
    ExtractMetadata,
}
