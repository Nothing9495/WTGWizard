using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WTGWizard.Main;
using WTGWizard.Shared.Services.DiskServices;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages.Steps;

public sealed partial class DeployMethodPage : Page, ITabActivatable
{
    private readonly IDiskIOService _diskIO = App.Services.GetRequiredService<IDiskIOService>();
    private bool _syncingDiskSelection;
    private bool _watcherSubscribed;
    private int _refreshSeq;
    public WizardViewModel VM { get; private set; } = null!;

    public DeployMethodPage()
    {
        VM = App.Services.GetRequiredService<WizardViewModel>();
        InitializeComponent();
    }

    // ══════════════════════════════════════════════════════
    //  页面生命周期
    // ══════════════════════════════════════════════════════

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is WizardViewModel vm)
        {
            VM = vm;
            DataContext = VM;
        }

        await RefreshDiskStateAsync();
        SubscribeWatcher();
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        UnsubscribeWatcher();
    }

    // ══════════════════════════════════════════════════════
    //  Tab 生命周期（ITabActivatable）
    // ══════════════════════════════════════════════════════

    public void OnTabActivated()
    {
        SubscribeWatcher();
    }

    public void OnTabDeactivated()
    {
        UnsubscribeWatcher();
    }

    private void SubscribeWatcher()
    {
        if (_watcherSubscribed) return;
        _watcherSubscribed = true;
        _diskIO.DisksChanged += OnDisksChanged;
        _diskIO.StartWatcher();
    }

    private void UnsubscribeWatcher()
    {
        if (!_watcherSubscribed) return;
        _watcherSubscribed = false;
        _diskIO.DisksChanged -= OnDisksChanged;
        _diskIO.StopWatcher();
    }

    // ══════════════════════════════════════════════════════
    //  事件处理
    // ══════════════════════════════════════════════════════

    private void OnDisksChanged()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await RefreshDiskStateAsync();
        });
    }

    // ══════════════════════════════════════════════════════
    //  磁盘选择
    // ══════════════════════════════════════════════════════

    private async void OnDiskRefreshClick(object sender, RoutedEventArgs e)
    {
        if (!DiskRefreshButton.IsEnabled) return;
        DiskRefreshButton.IsEnabled = false;
        try
        {
            await RefreshDiskStateAsync();
            await Task.Delay(1000);
        }
        finally
        {
            DiskRefreshButton.IsEnabled = true;
        }
    }

    private async void OnDiskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingDiskSelection) return;
        var combo = (ComboBox)sender;
        if (combo.SelectedItem is DiskBasicInfo disk)
        {
            // 恢复场景：VM 已持有此磁盘，跳过初始化
            if (VM.Method.SelectedDisk?.DeviceId == disk.DeviceId)
                return;

            // 用户手动选磁盘：完整初始化链
            // VM.OnSelectedDiskChanged 自动重置分区配置
            VM.Method.SelectedDisk = disk;
            VM.Method.DiskSafetyError = null;

            // 设置 UI 控件默认值
            EspSizeBox.Value = 300;
            OsSizeBox.Value = VM.Method.MaxOsDriveSize;
            // ReservedSwitch.IsOn = false;
            ReservedFsComboBox.SelectedIndex = 0;

            // 默认选中全新安装
            MethodRadioButtons.SelectedIndex = 0;

            // 加载分区列表
            await LoadPartitionsAsync(disk.Index);

            // 数据安全检测
            var danger = await _diskIO.CheckDiskSafetyAsync(disk.DeviceId);
            VM.Method.DiskSafetyError = danger;
        }
        else
        {
            // Clear() 触发的 null 选中（Items 为空）：忽略
            if (combo.Items.Count == 0) return;

            // 用户手动取消选中：清理状态
            VM.Method.DiskSafetyError = null;
            VM.Method.SelectedDisk = null;
            ResetToInitialState();
        }
    }

    // ══════════════════════════════════════════════════════
    //  部署方式切换
    // ══════════════════════════════════════════════════════

    private void DeployMethod_MethodSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var rb = (RadioButtons)sender;
        if (rb.SelectedItem is null) return;

        var index = rb.Items.IndexOf(rb.SelectedItem);
        if (index == 0)
            VM.Method.IsCleanInstall = true;
        else
            VM.Method.IsCleanInstall = false;
    }

    // ══════════════════════════════════════════════════════
    //  分区配置
    // ══════════════════════════════════════════════════════

    private void OnEspSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(sender.Value))
        {
            sender.Value = 300;
            return;
        }
        VM.Method.EfiPartSize = (int)sender.Value;
    }

    private void OnOsSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(sender.Value))
            return;
        VM.Method.OsDriveSize = sender.Value;
    }

    private void OnReservedSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
            VM.Method.EnableReservedVol = toggle.IsOn;
    }

    private void OnReservedFsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReservedFsComboBox.SelectedItem is ComboBoxItem item && item.Tag is string fs)
            VM.Method.ReservedDriveFs = fs;
    }

    // ══════════════════════════════════════════════════════
    //  刷新逻辑
    // ══════════════════════════════════════════════════════

    private async Task RefreshDiskStateAsync()
    {
        var seq = ++_refreshSeq;

        // ── 阶段 0：保存当前选中状态 ──
        var prevDiskId = VM.Method.SelectedDisk?.DeviceId;
        var prevPartNum = VM.Method.SelectedPartition?.PartitionNumber;

        // ── 阶段 1：枚举磁盘 ──
        var disks = await _diskIO.EnumerateExternalDisksAsync();
        if (seq != _refreshSeq) return;

        // 就地更新集合
        _syncingDiskSelection = true;
        VM.Method.Disks.Clear();
        foreach (var d in disks) VM.Method.Disks.Add(d);
        DiskListComboBox.Items.Clear();
        foreach (var d in disks) DiskListComboBox.Items.Add(d);
        _syncingDiskSelection = false;

        // ── 阶段 2：恢复磁盘选中 ──
        if (prevDiskId is not null)
        {
            var match = VM.Method.Disks.FirstOrDefault(d => d.DeviceId == prevDiskId);
            if (match is not null)
            {
                VM.Method.SelectedDisk = match;
                DiskListComboBox.SelectedItem = match;
                await LoadPartitionsAsync(match.Index);
                if (seq != _refreshSeq) return;

                // ── 阶段 3：恢复分区选中 ──
                if (prevPartNum is not null)
                {
                    var partMatch = VM.Method.Partitions.FirstOrDefault(p => p.PartitionNumber == prevPartNum);
                    if (partMatch is not null)
                    {
                        VM.Method.SelectedPartition = partMatch;
                        InstallPartitionComboBox.SelectedItem = partMatch;
                    }
                }
            }
            else
            {
                VM.Method.SelectedDisk = null;
                ResetToInitialState();
            }
        }
    }

    private async Task LoadPartitionsAsync(uint diskIndex)
    {
        try
        {
            var partitions = await _diskIO.GetPartitionsAsync(diskIndex, skipEsp: true);
            VM.Method.Partitions.Clear();
            foreach (var p in partitions) VM.Method.Partitions.Add(p);
        }
        catch (Exception)
        {
            // 分区枚举失败
        }
    }

    // ══════════════════════════════════════════════════════
    //  重置
    // ══════════════════════════════════════════════════════

    private void ResetToInitialState()
    {
        MethodRadioButtons.SelectedIndex = -1;
        VM.Method.Partitions.Clear();
    }

    // InfoBar ActionButton
    private void InfoBar_NoDriveLetter_ActionBtn_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = "diskmgmt.msc",
                UseShellExecute = true
            }
        );
    }
}
