using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using WTGWizard.Helpers;

namespace WTGWizard.ViewModels;

/// <summary>
/// Step 4 状态：高级设置（驱动集成、应答文件、启动选项）。
/// </summary>
public sealed partial class AdvancedOptionsVM : ObservableObject
{
    // ═══ 驱动集成 ═══
    [ObservableProperty] public partial bool DriverEnabled { get; set; }
    [ObservableProperty] public partial string? DriverPath { get; set; }
    [ObservableProperty] public partial bool ForceUnsigned { get; set; }

    // ═══ 应答文件 ═══
    [ObservableProperty] public partial bool CustomAnsFileEnabled { get; set; }
    [ObservableProperty] public partial string? AnsFilePath { get; set; }
    [ObservableProperty] public partial bool CleanImageAnsFile { get; set; }

    // ═══ BootEx ═══
    [ObservableProperty] public partial bool EnableBootEx { get; set; }
    [ObservableProperty] public partial bool EnableBootVerbose { get; set; }
    [ObservableProperty] public partial string? ImageBuildNumber { get; set; }

    /// <summary>镜像构建号是否满足 BootEx 最低要求。</summary>
    public bool IsBootExAvailable => WindowsBuildHelper.MeetsBootExThreshold(ImageBuildNumber);

    // ═══ AnsFile 指示器 ═══
    [ObservableProperty] public partial string AnsFileExistsDisplay { get; set; } = string.Empty;
    [ObservableProperty] public partial string AnsFileDescriptionFull { get; set; } = string.Empty;
    [ObservableProperty] public partial Style? AnsFileBadgeStyle { get; set; }

    // ═══ 验证 ═══
    public bool IsValid =>
        (!DriverEnabled || !string.IsNullOrEmpty(DriverPath))
        && (!CustomAnsFileEnabled || !string.IsNullOrEmpty(AnsFilePath));

    // ═══ 变更通知链 ═══

    partial void OnImageBuildNumberChanged(string? value)
    {
        OnPropertyChanged(nameof(IsBootExAvailable));
        if (!IsBootExAvailable) EnableBootEx = false;
    }

    partial void OnDriverEnabledChanged(bool value) => OnPropertyChanged(nameof(IsValid));
    partial void OnDriverPathChanged(string? value) => OnPropertyChanged(nameof(IsValid));
    partial void OnCustomAnsFileEnabledChanged(bool value) => OnPropertyChanged(nameof(IsValid));
    partial void OnAnsFilePathChanged(string? value) => OnPropertyChanged(nameof(IsValid));

    // ═══ 方法 ═══

    /// <summary>更新 AnsFile 指示器状态（由 WizardViewModel 在 Image 变化时调用）。</summary>
    public void UpdateAnsFileIndicator(ImageConfigVM image)
    {
        if (!image.HasImage)
        {
            AnsFileExistsDisplay = Lang.Page_WizStep_AdvOptions_AnsFile_NoImage;
            AnsFileBadgeStyle = (Style)Application.Current.Resources["CriticalIconInfoBadgeStyle"];
        }
        else if (image.AnsFileFoundPaths.Count == 0)
        {
            AnsFileExistsDisplay = Lang.Page_WizStep_AdvOptions_AnsFile_NotFound;
            AnsFileBadgeStyle = (Style)Application.Current.Resources["SuccessIconInfoBadgeStyle"];
        }
        else
        {
            AnsFileExistsDisplay = string.Format(Lang.Page_WizStep_AdvOptions_AnsFile_Found, image.AnsFileFoundPaths.Count);
            AnsFileBadgeStyle = (Style)Application.Current.Resources["AttentionIconInfoBadgeStyle"];
        }

        var pathDisplay = image.AnsFileFoundPaths.Count > 0
            ? string.Join("\n", image.AnsFileFoundPaths)
            : Lang.Page_WizStep_AdvOptions_AnsFile_None;

        AnsFileDescriptionFull = string.Format(Lang.Page_WizStep_AdvOptions_AnsFile_Desc, pathDisplay);
    }
}
