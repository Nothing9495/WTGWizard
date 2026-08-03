using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WTGWizard.Main.Language;
using WTGWizard.Shared.Services.WimService;

namespace WTGWizard.ViewModels;

/// <summary>映像校验状态。</summary>
public enum VerifyStatus
{
    /// <summary>未校验（默认）。</summary>
    Idle,
    /// <summary>校验中。</summary>
    Verifying,
    /// <summary>校验通过。</summary>
    Succeeded,
    /// <summary>校验未通过（映像内容损坏）。</summary>
    NotPass,
    /// <summary>校验失败（打开失败等 wimlib 错误）。</summary>
    Failed,
    /// <summary>校验时发生未知错误。</summary>
    Unknown,
}

/// <summary>
/// Step 1 状态：映像选择 + 元数据。
/// </summary>
public sealed partial class ImageConfigVM : ObservableObject
{
    [ObservableProperty] public partial string FilePath { get; set; } = string.Empty;
    [ObservableProperty] public partial int SelectedIndex { get; set; }
    [ObservableProperty] public partial ObservableCollection<string> Indices { get; set; } = [];

    [ObservableProperty] public partial ImageInfo? ImageInfo { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial IReadOnlyList<string> AnsFileFoundPaths { get; set; } = [];

    // ═══ 映像校验状态 ═══
    [ObservableProperty] public partial VerifyStatus VerifyStatus { get; set; }
    [ObservableProperty] public partial double VerifyProgress { get; set; }
    [ObservableProperty] public partial string? VerifyMessage { get; set; }

    // ═══ 打开映像状态 ═══
    [ObservableProperty] public partial bool ShowOpenError { get; set; }
    [ObservableProperty] public partial string? OpenErrorMessage { get; set; }

    public bool IsValid => !string.IsNullOrEmpty(FilePath) && ImageInfo is not null && !IsLoading
        && VerifyStatus != VerifyStatus.Verifying
        && !ShowOpenError;
    public bool HasImage => !string.IsNullOrEmpty(FilePath);
    public bool HasUnattend => AnsFileFoundPaths.Count > 0;

    /// <summary>校验进行中是否允许更换映像文件。</summary>
    public bool CanSelectImage => VerifyStatus != VerifyStatus.Verifying;

    /// <summary>是否可以开始校验（映像已选且打开无错误）。</summary>
    public bool CanVerify => HasImage && !ShowOpenError;

    /// <summary>校验进度百分比显示文本（如 "42%"）。</summary>
    public string VerifyProgressDisplay => $"{VerifyProgress:F0}%";

    /// <summary>校验通过 InfoBar 是否显示（绑定 IsOpen，解决关闭后无法重新显示问题）。</summary>
    public bool ShowVerifySuccess => VerifyStatus == VerifyStatus.Succeeded;

    /// <summary>校验未通过（内容损坏）提示消息（含失败原因）。</summary>
    public string VerifyNotPassMessage =>
        string.Format(Lang.InfoBar_ImageVerificationNotPass_Message, VerifyMessage);

    /// <summary>校验失败（打开失败等）提示消息。</summary>
    public string VerifyFailedMessage =>
        string.Format(Lang.InfoBar_ImageVerificationFailed_Message, VerifyMessage);

    /// <summary>校验时未知错误提示消息。</summary>
    public string VerifyUnknownMessage =>
        string.Format(Lang.InfoBar_ImageVerificationUnknown_Message, VerifyMessage);

    /// <summary>打开映像失败提示消息（含错误原因）。</summary>
    public string OpenErrorDisplayMessage =>
        string.Format(Lang.InfoBar_ImageOpenFailed_Message, OpenErrorMessage);

    /// <summary>当前选中的真实 WIM 索引（从 Indices 列表解析，1-based）。</summary>
    public int WimIndex =>
        SelectedIndex >= 0 && SelectedIndex < Indices.Count
        && int.TryParse(Indices[SelectedIndex], out var idx)
            ? idx
            : 0;

    /// <summary>映像展开大小（GB），用作 OS 分区最小值。</summary>
    public double ImageExpandedSizeGB => ImageInfo?.ExpandedSizeGB ?? 0;

    partial void OnFilePathChanged(string value)
    {
        AnsFileFoundPaths = [];
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(CanVerify));
        OnPropertyChanged(nameof(IsValid));
    }

    partial void OnImageInfoChanged(ImageInfo? value)
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ImageExpandedSizeGB));
    }
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsValid));
    partial void OnAnsFileFoundPathsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasUnattend));
    partial void OnVerifyStatusChanged(VerifyStatus value)
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ShowVerifySuccess));
        OnPropertyChanged(nameof(CanSelectImage));
    }
    partial void OnVerifyProgressChanged(double value) => OnPropertyChanged(nameof(VerifyProgressDisplay));
    partial void OnVerifyMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(VerifyNotPassMessage));
        OnPropertyChanged(nameof(VerifyFailedMessage));
        OnPropertyChanged(nameof(VerifyUnknownMessage));
    }
    partial void OnOpenErrorMessageChanged(string? value) => OnPropertyChanged(nameof(OpenErrorDisplayMessage));
    partial void OnShowOpenErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(CanVerify));
        OnPropertyChanged(nameof(IsValid));
    }
}
