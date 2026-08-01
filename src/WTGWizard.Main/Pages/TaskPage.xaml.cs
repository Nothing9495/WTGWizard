using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WTGWizard.Main;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Shared.Services;
using WTGWizard.Shared.Services.DiskServices;
using WTGWizard.Shared.Services.Logger;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages;

public sealed partial class TaskPage : Page, ITabActivatable
{
    private DeploymentOrchestrator? _orchestrator;
    private DiskPerformanceMonitor? _diskMonitor;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cts;
    private string _pendingSnapshot = string.Empty;
    private string _lastFlushedSnapshot = string.Empty;
    private bool _isFrozen;
    private bool _isConnected;

    private readonly DispatcherTimer _flushTimer;

    public WizardViewModel VM { get; }

    public TaskPage()
    {
        VM = App.Services.GetRequiredService<WizardViewModel>();
        InitializeComponent();

        // 100ms 节流：合并高频 OutputUpdated 事件为一次 UI 更新
        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _flushTimer.Tick += FlushPendingOutput;
    }

    /// <summary>
    /// Tab 切入时调用（替代 OnNavigatedTo）。
    /// </summary>
    public void OnTabActivated()
    {
        StartDeployment();
    }

    /// <summary>
    /// Tab 切出时调用（替代 OnNavigatedFrom）。
    /// </summary>
    public void OnTabDeactivated()
    {
        DisconnectUI();
    }

    private void StartDeployment()
    {
        var incoming = VM.TakeOrchestrator();

        // ── 无待执行任务 ──
        if (incoming is null)
        {
            // 返回已有部署页面，重连 UI
            if (_orchestrator is not null)
                ConnectUI();
            return;
        }

        // ── 新的 orchestrator：完全重置 ──
        PrepareNewDeployment(incoming);
        _ = RunDeploymentAsync();
    }

    private void PrepareNewDeployment(DeploymentOrchestrator incoming)
    {
        // 取消旧部署（如有）
        _cts?.Cancel();
        _cts?.Dispose();

        DisconnectUI();
        TerminalOutputBuffer.Shared.Clear();

        _orchestrator = incoming;
        TaskList.Items.Clear();
        foreach (var task in _orchestrator.Tasks)
            TaskList.Items.Add(task);

        Terminal.Clear();
        _lastFlushedSnapshot = string.Empty;
        _pendingSnapshot = string.Empty;
        _isFrozen = false;

        ConnectUI();

        StartDiskMonitor(_orchestrator.DiskNumber);
        _cts = new CancellationTokenSource();
    }

    private async Task RunDeploymentAsync()
    {
        try
        {
            await _orchestrator!.StartAsync(_cts!.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* 已由 DeploymentOrchestrator 记录 */ }
        finally
        {
            FlushPendingOutput(null, EventArgs.Empty);
            StopDiskMonitor();
            DisconnectUI();
            _cts?.Dispose();
            _cts = null;
            VM.IsDeploying = false;
            _orchestrator?.Dispose();
        }
    }

    // ══════════════════════════════════════════════════════
    //  UI 连接/断开
    // ══════════════════════════════════════════════════════

    private void ConnectUI()
    {
        if (_isConnected) return;
        _isConnected = true;

        // 同步 _isFrozen 与 Toggle 控件状态（Tab 切换后 Toggle 可能被重置）
        _isFrozen = FreezeToggle.IsChecked == true;

        // 回放已有输出
        string snapshot = TerminalOutputBuffer.Shared.Snapshot;
        if (!string.IsNullOrEmpty(snapshot))
        {
            Terminal.Clear();
            Terminal.Append(snapshot);
            _lastFlushedSnapshot = snapshot;
            _pendingSnapshot = snapshot;
        }

        TerminalOutputBuffer.Shared.OutputUpdated += OnOutputUpdated;
        _flushTimer.Start();
    }

    private void DisconnectUI()
    {
        if (!_isConnected) return;
        _isConnected = false;
        TerminalOutputBuffer.Shared.OutputUpdated -= OnOutputUpdated;
        _flushTimer.Stop();
    }

    private void OnOutputUpdated(string snapshot)
    {
        lock (_syncRoot)
        {
            _pendingSnapshot = snapshot ?? string.Empty;
        }
    }

    // ══════════════════════════════════════════════════════
    //  节流输出刷新
    // ══════════════════════════════════════════════════════

    private void FlushPendingOutput(object? sender, object e)
    {
        string snapshot;
        lock (_syncRoot)
        {
            snapshot = _pendingSnapshot;
            if (snapshot == _lastFlushedSnapshot) return;
        }

        if (!_isFrozen)
        {
            if (string.IsNullOrEmpty(snapshot))
            {
                Terminal.Clear();
            }
            else if (snapshot.Length > _lastFlushedSnapshot.Length)
            {
                string delta = snapshot.Substring(_lastFlushedSnapshot.Length);
                Terminal.Append(delta);
            }
            else if (snapshot.Length < _lastFlushedSnapshot.Length)
            {
                Terminal.Clear();
                Terminal.Append(snapshot);
            }

            lock (_syncRoot)
            {
                _lastFlushedSnapshot = snapshot;
            }
        }
    }

    // ══════════════════════════════════════════════════════
    //  磁盘性能监控
    // ══════════════════════════════════════════════════════

    private void StartDiskMonitor(uint diskNumber)
    {
        StopDiskMonitor();

        DiskNumText.Text = diskNumber.ToString();
        var monitor = new DiskPerformanceMonitor(diskNumber,
            App.Services.GetRequiredService<ILoggerService>());
        monitor.Updated += OnDiskPerfUpdated;
        monitor.Start();
        _diskMonitor = monitor;
    }

    private void StopDiskMonitor()
    {
        if (_diskMonitor is null) return;

        _diskMonitor.Updated -= OnDiskPerfUpdated;
        _diskMonitor.Dispose();
        _diskMonitor = null;

        DiskNumText.Text = "-";
        DiskBusyText.Text = "-";
        DiskReadWriteText.Text = "-";
    }

    private void OnDiskPerfUpdated(DiskPerformanceSnapshot snapshot)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            DiskBusyText.Text = snapshot.BusyDisplay;
            DiskReadWriteText.Text = snapshot.ReadWriteDisplay;
        });
    }

    // ══════════════════════════════════════════════════════
    //  工具栏事件处理
    // ══════════════════════════════════════════════════════

    private void WrapToggle_Changed(object sender, RoutedEventArgs e)
    {
        Terminal.TerminalTextWrapping = WrapToggle.IsChecked == true
            ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    private void FreezeToggle_Changed(object sender, RoutedEventArgs e)
    {
        _isFrozen = FreezeToggle.IsChecked == true;
        if (!_isFrozen)
            FlushPendingOutput(null, EventArgs.Empty);
    }

    private void AbortButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }
}
