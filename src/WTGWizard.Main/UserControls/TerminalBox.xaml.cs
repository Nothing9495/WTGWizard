using System;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace WTGWizard.UserControls;

public sealed partial class TerminalBox : UserControl
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private bool _userInteracting;
    private bool _needsScrollToEnd;

    public TerminalBox()
    {
        InitializeComponent();

        OutputScrollViewer.HorizontalScrollBarVisibility =
            TerminalTextWrapping == TextWrapping.NoWrap
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;

        IsTabStop = true;
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.C
            && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            string text = GetText();
            if (text.Length > 0)
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
            }
            e.Handled = true;
        }
    }

    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.Register(nameof(AutoScroll), typeof(bool), typeof(TerminalBox),
            new PropertyMetadata(true));

    public bool AutoScroll
    {
        get => (bool)GetValue(AutoScrollProperty);
        set => SetValue(AutoScrollProperty, value);
    }

    public static readonly DependencyProperty TerminalTextWrappingProperty =
        DependencyProperty.Register(nameof(TerminalTextWrapping), typeof(TextWrapping), typeof(TerminalBox),
            new PropertyMetadata(TextWrapping.Wrap, OnTerminalTextWrappingChanged));

    public TextWrapping TerminalTextWrapping
    {
        get => (TextWrapping)GetValue(TerminalTextWrappingProperty);
        set => SetValue(TerminalTextWrappingProperty, value);
    }

    private static void OnTerminalTextWrappingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (TerminalBox)d;
        var wrapping = (TextWrapping)e.NewValue;
        box.OutputScrollViewer.HorizontalScrollBarVisibility =
            wrapping == TextWrapping.NoWrap
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;
    }

    /// <summary>Append text (thread-safe).</summary>
    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (_dispatcher.HasThreadAccess)
            AppendToRichText(text);
        else
            _dispatcher.TryEnqueue(() => AppendToRichText(text));
    }

    /// <summary>Clear all content.</summary>
    public void Clear()
    {
        if (_dispatcher.HasThreadAccess)
            OutputRichTextBlock.Blocks.Clear();
        else
            _dispatcher.TryEnqueue(() => OutputRichTextBlock.Blocks.Clear());
    }

    /// <summary>Scroll to the end.</summary>
    public void ScrollToEnd()
    {
        RequestScrollToEnd();
    }

    /// <summary>Get all text content.</summary>
    public string GetText()
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var block in OutputRichTextBlock.Blocks)
        {
            if (block is Paragraph para)
            {
                if (!first) sb.Append('\n');
                first = false;

                foreach (var inline in para.Inlines)
                {
                    if (inline is Run run)
                        sb.Append(run.Text);
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>Current paragraph count.</summary>
    public int LineCount => OutputRichTextBlock.Blocks.Count;

    private void AppendToRichText(string text)
    {
        string[] lines = text.Split('\n');

        foreach (string line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;

            var para = new Paragraph();
            para.Inlines.Add(new Run { Text = line.TrimEnd('\r') });
            OutputRichTextBlock.Blocks.Add(para);
        }

        while (OutputRichTextBlock.Blocks.Count > 5000)
            OutputRichTextBlock.Blocks.RemoveAt(0);

        if (AutoScroll)
            RequestScrollToEnd();
    }

    private void RequestScrollToEnd()
    {
        if (_needsScrollToEnd) return;
        _needsScrollToEnd = true;
        OutputScrollViewer.LayoutUpdated += OnScrollLayoutUpdated;
    }

    private void OnScrollLayoutUpdated(object? sender, object e)
    {
        OutputScrollViewer.LayoutUpdated -= OnScrollLayoutUpdated;
        if (!_needsScrollToEnd) return;
        _needsScrollToEnd = false;
        OutputScrollViewer.ChangeView(null, OutputScrollViewer.ScrollableHeight, null);
    }

    private void OutputScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        bool nearBottom = OutputScrollViewer.ScrollableHeight - OutputScrollViewer.VerticalOffset <= 4;
        if (_userInteracting && nearBottom)
        {
            _userInteracting = false;
            AutoScroll = true;
        }
        else if (_userInteracting)
        {
            AutoScroll = false;
        }
    }

    private void OutputScrollViewer_PointerPressed(object? sender, PointerRoutedEventArgs e) =>
        _userInteracting = true;

    private void OutputScrollViewer_PointerWheelChanged(object? sender, PointerRoutedEventArgs e) =>
        _userInteracting = true;
}
