namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 分区基本信息 — 简化数据模型，供服务层使用。
/// </summary>
public sealed record PartitionBasicInfo(
    uint DiskNumber,
    uint PartitionNumber,
    ulong Size,
    string? DriveLetter,
    string? VolumeLabel)
{
    /// <summary>ComboBox 显示文本。</summary>
    public string DisplayName
    {
        get
        {
            var letter = string.IsNullOrEmpty(DriveLetter) ? "?" : DriveLetter;
            var label = string.IsNullOrWhiteSpace(VolumeLabel) ? "No Label" : VolumeLabel;
            var sizeGiB = Size / DiskConstants.BytesPerGiB;
            return $"{letter}: {label} ({sizeGiB:F2} GiB)";
        }
    }
}
