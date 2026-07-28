using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using WTGWizard.Shared.Services.Wim;

namespace WTGWizard.ViewModels;

/// <summary>
/// Step 1 状态：映像选择 + 元数据。
/// </summary>
public sealed partial class ImageConfig : ObservableObject
{
    [ObservableProperty] public partial string FilePath { get; set; } = string.Empty;
    [ObservableProperty] public partial int SelectedIndex { get; set; }
    private string[] _indices = [];

    public string[] Indices
    {
        get => _indices;
        set { _indices = value; OnPropertyChanged(); }
    }

    [ObservableProperty] public partial ImageInfo? ImageInfo { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial IReadOnlyList<string> AnsFileFoundPaths { get; set; } = [];

    // ═══ InfoBar 状态 ═══
    [ObservableProperty] public partial bool ShowVerifyError { get; set; }
    [ObservableProperty] public partial string? VerifyMessage { get; set; }

    public bool IsValid => !string.IsNullOrEmpty(FilePath) && ImageInfo is not null && !IsLoading;
    public bool HasImage => !string.IsNullOrEmpty(FilePath);
    public bool HasUnattend => AnsFileFoundPaths.Count > 0;

    partial void OnFilePathChanged(string value)
    {
        AnsFileFoundPaths = [];
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(IsValid));
    }

    partial void OnImageInfoChanged(ImageInfo? value) => OnPropertyChanged(nameof(IsValid));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsValid));
    partial void OnAnsFileFoundPathsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasUnattend));
}
