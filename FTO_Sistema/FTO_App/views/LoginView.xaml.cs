using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FTO_App.Services;

namespace FTO_App.Views
{
    public partial class LoginView : UserControl
    {
        public event EventHandler<string> OnLoginSuccess; // Evento para avisar MainWindow
        private bool _suppressDeviceSave;

        public LoginView()
        {
            InitializeComponent();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string u = TxtLoginUser.Text;
            string p = TxtLoginPass.Password;

            if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p)) 
            { 
                MessageBox.Show("Preencha os campos.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error); 
                return; 
            }

            try
            {
                using (var conn = Database.GetConnection())
                using (var cmd = new SQLiteCommand("SELECT Id FROM Users WHERE User = @u AND Senha = @p", conn))
                {
                    cmd.Parameters.AddWithValue("@u", u);
                    cmd.Parameters.AddWithValue("@p", p);
                    if (cmd.ExecuteScalar() != null)
                    {
                        LblWelcome.Text = $"Bem-vindo, {u}!";
                        LoginGrid.Visibility = Visibility.Collapsed;
                        SelectionGrid.Visibility = Visibility.Visible;
                        LoadDeviceLists();
                    }
                    else 
                    {
                        MessageBox.Show("Dados incorretos.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show($"Erro login: {ex.Message}"); }
        }

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            string u = TxtRegUser.Text;
            string p = TxtRegPass.Password;
            if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p)) return;

            try
            {
                Database.ExecuteNonQuery("INSERT INTO Users (User, Senha) VALUES (@u, @p)",
                    new Dictionary<string, object> {{"@u", u}, {"@p", p}});
                MessageBox.Show("Usuário criado!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                BtnVoltarLogin_Click(null, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}");
            }
        }

        private void BtnIrParaRegistro_Click(object sender, RoutedEventArgs e)
        {
            PanelLogin.Visibility = Visibility.Collapsed;
            PanelRegister.Visibility = Visibility.Visible;
            TxtRegUser.Focus();
        }

        private void BtnVoltarLogin_Click(object sender, RoutedEventArgs e)
        {
            PanelRegister.Visibility = Visibility.Collapsed;
            PanelLogin.Visibility = Visibility.Visible;
            TxtLoginUser.Focus();
        }

        private void BtnGoToSales_Click(object sender, RoutedEventArgs e)
        {
            // Dispara evento para a MainWindow trocar a tela
            OnLoginSuccess?.Invoke(this, TxtLoginUser.Text);
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            TxtLoginPass.Password = "";
            SelectionGrid.Visibility = Visibility.Collapsed;
            LoginGrid.Visibility = Visibility.Visible;
        }

        private void LoadDeviceLists()
        {
            _suppressDeviceSave = true;
            try
            {
                var printers = InstalledDevicesService.GetPrinters();
                CbImpressora.ItemsSource = printers;

                if (!string.IsNullOrWhiteSpace(DeviceSettingsStore.Current.SelectedPrinter) &&
                    printers.Contains(DeviceSettingsStore.Current.SelectedPrinter))
                {
                    CbImpressora.SelectedItem = DeviceSettingsStore.Current.SelectedPrinter;
                }
                else
                {
                    string? preferred = InstalledDevicesService.FindPreferredThermalPrinter(printers);
                    if (preferred != null)
                    {
                        CbImpressora.SelectedItem = preferred;
                        DeviceSettingsStore.SetPrinter(preferred);
                    }
                    else if (printers.Count > 0)
                        CbImpressora.SelectedIndex = 0;
                    else
                        CbImpressora.Items.Add("(Nenhuma impressora encontrada)");
                }

                var scanners = InstalledDevicesService.GetScanners();
                CbScanner.ItemsSource = scanners;

                if (!string.IsNullOrWhiteSpace(DeviceSettingsStore.Current.SelectedScanner) &&
                    scanners.Any(s => s.Equals(DeviceSettingsStore.Current.SelectedScanner, StringComparison.OrdinalIgnoreCase)))
                {
                    CbScanner.SelectedItem = scanners.First(s =>
                        s.Equals(DeviceSettingsStore.Current.SelectedScanner, StringComparison.OrdinalIgnoreCase));
                }
                else if (scanners.Count > 0)
                    CbScanner.SelectedIndex = 0;
            }
            finally
            {
                _suppressDeviceSave = false;
            }
        }

        private void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            LoadDeviceLists();
            MessageBox.Show("Lista de dispositivos atualizada.", "Dispositivos", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CbImpressora_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDeviceSave || CbImpressora.SelectedItem is not string printer) return;
            if (printer.StartsWith("(", StringComparison.Ordinal)) return;
            try { DeviceSettingsStore.SetPrinter(printer); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void CbScanner_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDeviceSave || CbScanner.SelectedItem is not string scanner) return;
            if (scanner.StartsWith("(", StringComparison.Ordinal)) return;
            try { DeviceSettingsStore.SetScanner(scanner); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
}