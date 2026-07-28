using System;
using Microsoft.UI.Xaml.Controls;

namespace WTGWizard.Models;

/// <summary>
/// InfoBar 的动态状态快照。
/// </summary>
public sealed record InfoBarState(
    string Id,
    string Title,
    string Message,
    InfoBarSeverity Severity,
    bool IsOpen = true,
    bool IsClosable = true,
    string? ActionContent = null,
    EventHandler? ActionClicked = null);
