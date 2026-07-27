using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WTGWizard.Main;
using WTGWizard.Messages;

namespace WTGWizard.Pages;

public sealed partial class WelcomePage : Page
{
    public WelcomePage()
    {
        InitializeComponent();
    }

    private void GoWizard_Button_Click(object sender, RoutedEventArgs e)
    {
        // 发送导航消息到 MainWindow
        WeakReferenceMessenger.Default.Send(new NavigateToPageMessage("WizardPage"));
    }
}
