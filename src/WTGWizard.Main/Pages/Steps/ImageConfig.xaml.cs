using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using WTGWizard.Helpers;
using WTGWizard.Main;
using WTGWizard.Models;
using ManagedWimLib;
using WTGWizard.Shared.Services.WimService;
using WTGWizard.UserControls;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages.Steps;

public sealed partial class ImageConfigPage : Page
{
    private readonly IWimService _wimService = App.Services.GetRequiredService<IWimService>();
    public WizardViewModel VM { get; private set; } = null!;

    private int _refreshSeq;
    private CancellationTokenSource? _verifyCts;

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
        VM.Image.VerifyStatus = VerifyStatus.Idle;
        VM.Image.VerifyProgress = 0;
        VM.Image.ShowOpenError = false;
        VM.Image.FilePath = path;
        VM.Image.Indices = [];   // 清空旧索引：修复换文件期间索引切换竞态 + 确保 ComboBox 归 -1 使事件必然触发

        // 句柄占用（程序生命周期）：防止映像被更名/移动/写入（FileShare.Read 兼容 Worker 读取）
        if (!ImageFileGuard.Acquire(path, out var openErr))
        {
            VM.Image.OpenErrorMessage = openErr;
            VM.Image.ShowOpenError = true;
            ResetImageState();
            ImageInfoCard.CardState = ImageInfoCardState.Error;
            return;
        }

        try
        {
            var indices = await _wimService.EnumerateIndicesAsync(path);
            VM.Image.Indices = new ObservableCollection<string>(indices.Select(i => i.ToString()));

            // SelectedIndex -1 → 0 必然触发 SelectionChanged → RefreshImageStateAsync 加载（事件驱动，避免重复调用）
            if (indices.Count > 0)
                VM.Image.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            VM.Image.OpenErrorMessage = ex.Message;
            VM.Image.ShowOpenError = true;
            ResetImageState();
            ImageInfoCard.CardState = ImageInfoCardState.Error;
            return;
        }
    }

    private async void ImageIndexComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectedIndex 已通过 TwoWay 绑定自动更新
        await RefreshImageStateAsync();
    }

    /// <summary>
    /// 统一镜像信息加载入口 — 更新 UI + 加载元数据。
    /// 返回页面时重新加载（无路径缓存）；并发防护：刷新序号丢弃过期异步结果。
    /// </summary>
    private async System.Threading.Tasks.Task RefreshImageStateAsync()
    {
        if (VM?.Image?.FilePath is not { Length: > 0 } path) return;

        var indices = VM.Image.Indices;
        var pos = VM.Image.SelectedIndex;
        if (pos < 0 || pos >= indices.Count) return;
        if (!int.TryParse(indices[pos], out var index)) return;

        var seq = ++_refreshSeq;
        VM.Image.IsLoading = true;
        UpdateLoadingState(true);

        try
        {
            var info = await _wimService.GetImageInfo(path, index);
            if (seq != _refreshSeq) return;

            VM.Image.ShowOpenError = false;
            VM.Image.ImageInfo = info;
            UpdateImageInfoCard(info);
            VM.Image.AnsFileFoundPaths = info.AnsFilePaths;
        }
        catch (Exception ex)
        {
            if (seq != _refreshSeq) return;
            VM.Image.OpenErrorMessage = ex.Message;
            VM.Image.ShowOpenError = true;
            ResetImageState();
        }
        finally
        {
            VM.Image.IsLoading = false;
            UpdateLoadingState(false);
        }
    }

    // ══════════════════════════════════════════════════════
    //  打开失败状态清理（索引、元数据、校验状态、信息卡片）
    // ══════════════════════════════════════════════════════

    /// <summary>打开失败后清理映像选择状态（索引、元数据、校验状态、信息卡片）。</summary>
    private void ResetImageState()
    {
        VM.Image.Indices = [];
        VM.Image.ImageInfo = null;
        VM.Image.AnsFileFoundPaths = [];
        VM.Image.VerifyStatus = VerifyStatus.Idle;   // 作废校验结果
        VM.Image.VerifyProgress = 0;
        ClearImageInfoCard();
    }

    /// <summary>清空映像信息卡片（DP 默认值 "-"）。</summary>
    private void ClearImageInfoCard()
    {
        ImageInfoCard.MajorVersion = "-";
        ImageInfoCard.ImageIndex = "-";
        ImageInfoCard.ImageVersion = "-";
        ImageInfoCard.FeatureUpdate = "-";
        ImageInfoCard.Architecture = "-";
        ImageInfoCard.BuildNumber = "-";
        ImageInfoCard.ExpandedSize = "-";
        ImageInfoCard.DateCreated = "-";
        ImageInfoCard.ImageName = "-";
        ImageInfoCard.ImageDescription = "-";
        ImageInfoCard.DisplayDescription = "-";
        ImageInfoCard.LogoSource = null;
        ImageInfoCard.CardState = ImageInfoCardState.NoImage;
        UpdateLoadingState(false);
    }

    // ══════════════════════════════════════════════════════
    //  映像校验（手动触发，用户控制）
    // ══════════════════════════════════════════════════════

    private void VerifyButton_Click(object sender, RoutedEventArgs e)
        => _ = RunVerifyAsync();

    private async System.Threading.Tasks.Task RunVerifyAsync()
    {
        if (!VM.Image.HasImage) return;

        VM.Image.VerifyStatus = VerifyStatus.Verifying;
        VM.Image.VerifyProgress = 0;
        _verifyCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p => VM.Image.VerifyProgress = p);
            await _wimService.VerifyAsync(VM.Image.FilePath, progress, _verifyCts.Token);
            VM.Image.VerifyStatus = VerifyStatus.Succeeded;
        }
        catch (OperationCanceledException)
        {
            VM.Image.VerifyStatus = VerifyStatus.Idle;
        }
        catch (WimVerificationException ex)
        {
            VM.Image.VerifyMessage = ex.Message;
            VM.Image.VerifyStatus = VerifyStatus.NotPass;
        }
        catch (WimLibException ex)
        {
            VM.Image.VerifyMessage = Wim.GetErrorString(ex.ErrorCode);
            VM.Image.VerifyStatus = VerifyStatus.Failed;
        }
        catch (Exception ex)
        {
            VM.Image.VerifyMessage = ex.Message;
            VM.Image.VerifyStatus = VerifyStatus.Unknown;
        }
        finally
        {
            _verifyCts.Dispose();
            _verifyCts = null;
        }
    }

    private void CancelVerifyButton_Click(object sender, RoutedEventArgs e)
        => _verifyCts?.Cancel();

    private void VerifySuccessInfoBar_Closed(InfoBar sender, object args)
        => VM.Image.VerifyStatus = VerifyStatus.Idle;

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
        ImageInfoCard.CardState = ImageInfoCardState.Normal;
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
        if (loading)
            ImageInfoCard.CardState = ImageInfoCardState.Loading;
        // false：不设状态——结束态由各路径显式设置（成功→Normal；失败→NoImage/Error）
    }
}
