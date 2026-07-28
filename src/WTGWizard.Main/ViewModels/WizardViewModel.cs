using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTGWizard.Main.Language;
using WTGWizard.Messages;

namespace WTGWizard.ViewModels;

/// <summary>
/// Wizard 协调器 VM — 步骤导航 + 全局状态容器。
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    // ═══ 步骤导航 ═══

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoForward))]
    [NotifyPropertyChangedFor(nameof(IsCurrentStepValid))]
    public partial int CurrentStep { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoForward))]
    public partial bool IsDeploying { get; set; }
    [ObservableProperty] public partial string CurrentStepTitle { get; set; } = string.Empty;

    public bool CanGoBack => CurrentStep > 0 && !IsDeploying;
    public bool CanGoForward => CurrentStep < MaxStep && IsCurrentStepValid;
    public int MaxStep => 4; // 0-4, 共 5 步
    public int TotalSteps => 5;

    // ═══ 状态子对象 ═══

    public ImageConfigVM Image { get; } = new();
    public DeployOptionsVM Options { get; } = new();
    public DeployMethodVM Method { get; }
    public AdvancedOptionsVM Advanced { get; } = new();

    // ═══ 构造函数 ═══

    public WizardViewModel()
    {
        Method = new DeployMethodVM();
        Image.PropertyChanged += OnSubPropertyChanged;
        Image.PropertyChanged += OnImagePropertyChanged;
        Method.PropertyChanged += OnSubPropertyChanged;
        Advanced.PropertyChanged += OnSubPropertyChanged;
        Advanced.UpdateAnsFileIndicator(Image);
    }

    private void OnSubPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "IsValid")
        {
            OnPropertyChanged(nameof(IsCurrentStepValid));
            OnPropertyChanged(nameof(CanGoForward));
        }
    }

    private void OnImagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImageConfigVM.FilePath)
            or nameof(ImageConfigVM.AnsFileFoundPaths)
            or nameof(ImageConfigVM.HasImage)
            or nameof(ImageConfigVM.SelectedIndex))
        {
            Advanced.UpdateAnsFileIndicator(Image);
        }
    }

    // ═══ 派生属性 ═══

    public bool IsCurrentStepValid => CurrentStep switch
    {
        0 => Image.IsValid,
        1 => Method.IsValid,
        2 => true,
        3 => Advanced.IsValid,
        4 => true,
        _ => false
    };

    // ═══ 命令 ═══

    [RelayCommand]
    private void GoBack()
    {
        if (CanGoBack)
            CurrentStep--;
    }

    [RelayCommand]
    private void GoForward()
    {
        if (CanGoForward)
            CurrentStep++;
    }

    [RelayCommand]
    private void Reset()
    {
        CurrentStep = 0;
    }

    /// <summary>
    /// 请求导航到任务执行页（由 MainWindow 处理）。
    /// </summary>
    public event Action? NavigateToTaskRequested;

    [RelayCommand]
    private void StartDeploy()
    {
        // TODO: 创建 DeploymentOrchestrator
        IsDeploying = true;
        NavigateToTaskRequested?.Invoke();
    }

    // ═══ 步骤指示器 ═══

    /// <summary>
    /// 更新当前步骤标题（由 WizardHost 调用）。
    /// </summary>
    public void UpdateStepTitle(string[] stepResourceKeys)
    {
        if (CurrentStep >= 0 && CurrentStep < stepResourceKeys.Length)
        {
            CurrentStepTitle = Localization.GetString(stepResourceKeys[CurrentStep]);
        }
    }
}
