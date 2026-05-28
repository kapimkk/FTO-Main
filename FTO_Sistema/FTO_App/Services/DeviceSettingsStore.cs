using System;
using System.IO;
using System.Text.Json;

namespace FTO_App.Services
{
    /// <summary>
    /// Persistência das configurações de dispositivos em AppData.
    /// </summary>
    public static class DeviceSettingsStore
    {
        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FTO_Sistema");

        private static readonly string SettingsPath = Path.Combine(SettingsDir, "devices.json");

        public static DeviceSettings Current { get; private set; } = new DeviceSettings();

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                string json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<DeviceSettings>(json);
                if (loaded != null) Current = loaded;
            }
            catch
            {
                Current = new DeviceSettings();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Não foi possível salvar as configurações: {ex.Message}", ex);
            }
        }

        public static void SetPrinter(string? name)
        {
            Current.SelectedPrinter = name?.Trim() ?? string.Empty;
            Save();
        }

        public static void SetScanner(string? name)
        {
            Current.SelectedScanner = name?.Trim() ?? string.Empty;
            Save();
        }
    }
}
