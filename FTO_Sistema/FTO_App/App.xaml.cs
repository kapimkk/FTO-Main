using System.Windows;
using FTO_App.Services;

namespace FTO_App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DeviceSettingsStore.Load();
            base.OnStartup(e);
        }
    }
}
