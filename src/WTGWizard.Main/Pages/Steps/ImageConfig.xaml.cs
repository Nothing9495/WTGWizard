using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using WTGWizard.Helpers;
using WTGWizard.Main;
using WTGWizard.Models;
using WTGWizard.Shared.Services.Wim;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages.Steps;

public sealed partial class ImageConfigPage : Page
{
    private readonly IWimService _wimService = App.Services.GetRequiredService<IWimService>();
    private bool _syncingSelection;
    public WizardViewModel VM { get; private set; } = null!;

    public ImageConfigPage()
    {
        VM = App.Services.GetRequiredService<WizardViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is WizardViewModel vm)
            VM = vm;

        VM.Image.PropertyChanged += OnImagePropertyChanged;
        SyncItemsSource();
        SyncSelectedIndex();
        await RefreshImageStateAsync();
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        VM.Image.PropertyChanged -= OnImagePropertyChanged;
    }

    private void OnImagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImageConfig.Indices))
            SyncItemsSource();
        else if (e.PropertyName is nameof(ImageConfig.SelectedIndex))
            SyncSelectedIndex();
    }

    private void SyncItemsSource()
    {
        var savedIndex = ImageIndexComboBox.SelectedIndex;
        _syncingSelection = true;
        ImageIndexComboBox.Items.Clear();
        foreach (var item in VM.Image.Indices)
            ImageIndexComboBox.Items.Add(item);
        // 恢复选中：优先用索引（兜底 SelectedItem 引用可能不匹配）
        var targetIndex = VM.Image.SelectedIndex >= 0 ? VM.Image.SelectedIndex : savedIndex;
        if (targetIndex >= 0 && targetIndex < ImageIndexComboBox.Items.Count)
            ImageIndexComboBox.SelectedIndex = targetIndex;
        _syncingSelection = false;
    }

    private void SyncSelectedIndex()
    {
        if (_syncingSelection) return;
        var idx = VM.Image.SelectedIndex;
        if (idx == ImageIndexComboBox.SelectedIndex) return;
        _syncingSelection = true;
        if (idx >= 0 && idx < ImageIndexComboBox.Items.Count)
            ImageIndexComboBox.SelectedIndex = idx;
        _syncingSelection = false;
    }

    private async void ImageFilePicker_FileSelected(object sender, string path)
    {
        VerifyInfoBar.Info = null;
        VM.Image.FilePath = path;

        try
        {
            var indices = await _wimService.EnumerateIndicesAsync(path);
            VM.Image.Indices = indices.Select(i => i.ToString()).ToArray();

            if (indices.Count > 0)
                VM.Image.SelectedIndex = 0;
        }
        catch (Exception)
        {
            return;
        }

        await RefreshImageStateAsync();
    }

    private async void ImageIndexComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;

        var pos = ImageIndexComboBox.SelectedIndex;
        _syncingSelection = true;
        VM.Image.SelectedIndex = pos;
        _syncingSelection = false;

        await RefreshImageStateAsync();
    }

    /// <summary>
    /// 统一镜像信息加载入口 — 更新 UI + 加载元数据。
    /// 对标 Step2 的 RefreshDiskStateAsync 模式：显式调用，不依赖事件。
    /// </summary>
    private async System.Threading.Tasks.Task RefreshImageStateAsync()
    {
        if (VM?.Image?.FilePath is not { Length: > 0 } path) return;

        var indices = VM.Image.Indices;
        var pos = VM.Image.SelectedIndex;
        if (pos < 0 || pos >= indices.Length) return;
        if (!int.TryParse(indices[pos], out var index)) return;

        VM.Image.IsLoading = true;
        UpdateLoadingState(true);

        try
        {
            var info = await _wimService.GetImageInfo(path, index);
            VM.Image.ImageInfo = info;
            UpdateImageInfoCard(info);
            VM.Image.AnsFileFoundPaths = info.AnsFilePaths;
        }
        catch (Exception)
        {
            
        }
        finally
        {
            VM.Image.IsLoading = false;
            UpdateLoadingState(false);
        }

        // 异步校验映像完整性（不阻塞主流程）
        _ = VerifyImageAsync(path);
    }

    private async System.Threading.Tasks.Task VerifyImageAsync(string path)
    {
        try
        {
            await _wimService.VerifyAsync(path);
            VerifyInfoBar.Info = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Step1] 映像校验失败: {ex.Message}");
            VerifyInfoBar.Info = new InfoBarState(
                Id: "verify",
                Title: "Image Verification Failed",
                Message: ex.Message,
                Severity: Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                IsOpen: true,
                IsClosable: false);
        }
    }

    private void UpdateImageInfoCard(ImageInfo info)
    {
        ImageInfoCard.MajorVersion = info.MajorVersion.ToString();
        ImageInfoCard.ImageIndex = info.Index.ToString();
        ImageInfoCard.ImageVersion = info.FeatureVersion;
        ImageInfoCard.FeatureUpdate = info.FeatureVersion;
        ImageInfoCard.Architecture = info.Architecture;
        ImageInfoCard.BuildNumber = info.BuildNumber;
        ImageInfoCard.ExpandedSize = $"{info.ExpandedSizeGB:F1} GiB";
        ImageInfoCard.DateCreated = info.DateCreated;
        ImageInfoCard.ImageName = info.Name;
        ImageInfoCard.ImageDescription = info.Description;
        ImageInfoCard.DisplayDescription = info.DisplayDescription;

        ImageInfoCard.LogoSource = new BitmapImage(new Uri(GetWinLogoPath(info.BuildNumber)));
    }

    private static string GetWinLogoPath(string buildNumber)
    {
        var build = WindowsBuildHelper.TryGetBuildRevision(buildNumber)?.major ?? 0;
        if (build >= 22000) return "ms-appx:///Assets/WinLogo/Windows11.png";
        if (build >= 10240) return "ms-appx:///Assets/WinLogo/Windows10.png";
        if (build >= 9600) return "ms-appx:///Assets/WinLogo/Windows8.png";
        if (build >= 7600) return "ms-appx:///Assets/WinLogo/Windows7.png";
        return "ms-appx:///Assets/WinLogo/WindowsPH.png";
    }

    private void UpdateLoadingState(bool loading)
    {
        ImageInfoCard.LoadingVisible = loading ? Visibility.Visible : Visibility.Collapsed;
        ImageInfoCard.LogoVisible = loading ? Visibility.Collapsed : Visibility.Visible;
    }
}
