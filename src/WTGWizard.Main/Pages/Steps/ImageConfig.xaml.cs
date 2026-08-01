using System;
using System.Collections.ObjectModel;
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
using WTGWizard.Shared.Services.WimService;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages.Steps;

public sealed partial class ImageConfigPage : Page
{
    private readonly IWimService _wimService = App.Services.GetRequiredService<IWimService>();
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

        await RefreshImageStateAsync();
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
    }

    private async void ImageFilePicker_FileSelected(object sender, string path)
    {
        VM.Image.ShowVerifyError = false;
        VM.Image.FilePath = path;

        try
        {
            var indices = await _wimService.EnumerateIndicesAsync(path);
            VM.Image.Indices = new ObservableCollection<string>(indices.Select(i => i.ToString()));

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
        // SelectedIndex 已通过 TwoWay 绑定自动更新
        await RefreshImageStateAsync();
    }

    /// <summary>
    /// 统一镜像信息加载入口 — 更新 UI + 加载元数据。
    /// </summary>
    private async System.Threading.Tasks.Task RefreshImageStateAsync()
    {
        if (VM?.Image?.FilePath is not { Length: > 0 } path) return;

        var indices = VM.Image.Indices;
        var pos = VM.Image.SelectedIndex;
        if (pos < 0 || pos >= indices.Count) return;
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
            VM.Image.ShowVerifyError = false;
        }
        catch (Exception ex)
        {
            VM.Image.VerifyMessage = ex.Message;
            VM.Image.ShowVerifyError = true;
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
