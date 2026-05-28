namespace FTO_App.Services
{
    /// <summary>
    /// Preferências de impressora e scanner persistidas localmente.
    /// </summary>
    public class DeviceSettings
    {
        public string SelectedPrinter { get; set; } = string.Empty;
        public string SelectedScanner { get; set; } = string.Empty;
    }
}
