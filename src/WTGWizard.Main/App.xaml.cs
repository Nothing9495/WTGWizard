using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Text;

namespace WTGWizard;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    public App()
    {
        this.InitializeComponent();
        UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        string logFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WTGWizard", "log");
        Directory.CreateDirectory(logFolder);
        string logFile = Path.Combine(logFolder, $"WTGWizard_{DateTime.Now:yyMMdd}.log");

        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] App Crash:");
        sb.AppendLine(e.Exception.ToString());

        using var fs = File.Open(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var sw = new StreamWriter(fs);
        sw.Write(sb);
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        m_Window = new MainWindow();
        m_Window.Activate();
    }

    private Window? m_Window;
}
