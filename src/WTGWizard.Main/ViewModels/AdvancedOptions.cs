using CommunityToolkit.Mvvm.ComponentModel;

namespace WTGWizard.ViewModels;

/// <summary>
/// Step 4 状态：高级设置（驱动集成、应答文件、启动选项）。
/// </summary>
public sealed partial class AdvancedOptions : ObservableObject
{
    // ═══ 驱动集成 ═══
    [ObservableProperty] public partial bool DriverEnabled { get; set; }
    [ObservableProperty] public partial string? DriverPath { get; set; }
    [ObservableProperty] public partial bool ForceUnsigned { get; set; }

    // ═══ 应答文件 ═══
    [ObservableProperty] public partial bool CustomUnattendEnabled { get; set; }
    [ObservableProperty] public partial string? UnattendPath { get; set; }
    [ObservableProperty] public partial bool CleanImageUnattend { get; set; }

    // ═══ BootEx ═══
    [ObservableProperty] public partial bool EnableBootEx { get; set; }
    [ObservableProperty] public partial bool EnableBootVerbose { get; set; }

    // ═══ 验证 ═══
    public bool IsValid =>
        (!DriverEnabled || !string.IsNullOrEmpty(DriverPath))
        && (!CustomUnattendEnabled || !string.IsNullOrEmpty(UnattendPath));
}
