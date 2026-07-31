using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WTGWizard.Shared.Services.Wim;

namespace WTGWizard.ViewModels;

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

    // ═══ InfoBar 状态 ═══
    [ObservableProperty] public partial bool ShowVerifyError { get; set; }
    [ObservableProperty] public partial string? VerifyMessage { get; set; }

    public bool IsValid => !string.IsNullOrEmpty(FilePath) && ImageInfo is not null && !IsLoading;
    public bool HasImage => !string.IsNullOrEmpty(FilePath);
    public bool HasUnattend => AnsFileFoundPaths.Count > 0;

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
        OnPropertyChanged(nameof(IsValid));
    }

    partial void OnImageInfoChanged(ImageInfo? value)
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ImageExpandedSizeGB));
    }
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsValid));
    partial void OnAnsFileFoundPathsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasUnattend));
}
