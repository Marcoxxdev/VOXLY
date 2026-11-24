using System.Windows;

namespace VoiceClient;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show(e.Exception.Message, Loc.T("app_error_title"));
            e.Handled = true;
        };
    }
}
