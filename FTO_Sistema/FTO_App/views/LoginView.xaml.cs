using System;
using System.Collections.Generic;
using Npgsql;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FTO_App.Services;

namespace FTO_App.Views
{
    public partial class LoginView : UserControl
    {
        public event EventHandler<string> OnLoginSuccess; // Evento para avisar MainWindow
        public event EventHandler OnEstoqueRequest;
        public event EventHandler OnAnalyticsRequest;
        private bool _suppressDeviceSave;
        private bool _updateInProgress;

        public LoginView()
        {
            InitializeComponent();
            LblVersao.Text = $"Versão {UpdateService.GetLocalVersionDisplay()}";
            Loaded += (_, _) => MostrarResultadoUltimaAtualizacao();
        }

        /// <summary>
        /// Fecha o ciclo da atualização: o script roda escondido depois que o app fecha, então é
        /// na próxima abertura que o usuário fica sabendo se a troca de arquivos deu certo.
        /// </summary>
        private static void MostrarResultadoUltimaAtualizacao()
        {
            var resultado = UpdateService.ConsumirResultadoUltimaAtualizacao();
            if (resultado is null) return;

            string versao = UpdateService.GetLocalVersionDisplay();
            if (resultado.Value.Sucesso)
            {
                MessageBox.Show($"Sistema atualizado com sucesso para a {versao}.",
                    "Atualização", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show(
                "A última atualização NÃO foi aplicada.\n\n" +
                $"{resultado.Value.Mensagem}\n\n" +
                $"O sistema continua na {versao}. Feche outros programas que possam estar usando a pasta " +
                "do sistema e tente \"Atualizar sistema\" de novo.\n\n" +
                $"Detalhes: {UpdateService.CaminhoLogAtualizacao}",
                "Atualização", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private async void BtnAtualizarSistema_Click(object sender, RoutedEventArgs e)
        {
            if (_updateInProgress) return;

            _updateInProgress = true;
            BtnAtualizarSistema.IsEnabled = false;
            string originalLabel = BtnAtualizarSistema.Content?.ToString() ?? "↻ Atualizar sistema";

            try
            {
                BtnAtualizarSistema.Content = "Verificando...";
                UpdateCheckResult check = await UpdateService.CheckForUpdateAsync();

                if (!check.Success)
                {
                    MessageBox.Show(
                        check.ErrorMessage ?? "Não foi possível verificar atualizações.",
                        "Atualização",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!check.UpdateAvailable)
                {
                    string msg = string.IsNullOrWhiteSpace(check.ErrorMessage)
                        ? $"Você já está na versão mais recente.\n\nVersão atual: {check.LocalVersionDisplay}"
                        : check.ErrorMessage;
                    MessageBox.Show(msg, "Atualização", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string notes = string.IsNullOrWhiteSpace(check.ReleaseNotes)
                    ? ""
                    : $"\n\nNotas:\n{Truncate(check.ReleaseNotes, 400)}";

                var confirm = MessageBox.Show(
                    $"Nova versão disponível: {check.RemoteVersionDisplay}\n" +
                    $"Versão instalada: {check.LocalVersionDisplay}\n\n" +
                    $"O sistema será fechado, atualizado e reaberto automaticamente." +
                    $"\nSeus dados (.env e banco) serão preservados.{notes}",
                    "Atualizar sistema",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                    return;

                var progress = new Progress<string>(status =>
                {
                    BtnAtualizarSistema.Content = status;
                });

                await UpdateService.DownloadAndPrepareUpdateAsync(check, progress);

                MessageBox.Show(
                    "Download concluído. O aplicativo será fechado para aplicar a atualização.",
                    "Atualização",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Falha ao atualizar:\n\n{ex.Message}",
                    "Atualização",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _updateInProgress = false;
                BtnAtualizarSistema.Content = originalLabel;
                BtnAtualizarSistema.IsEnabled = true;
            }
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text;
            return text[..max] + "...";
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string u = TxtLoginUser.Text?.Trim() ?? "";
            string p = TxtLoginPass.Password;

            if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
            {
                MessageBox.Show("Preencha os campos.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                using var conn = Database.GetConnection();
                // O cadastro impede duplicidade por LOWER(User) — o login precisa usar o mesmo critério,
                // senão quem se cadastrou como "Admin" não entra digitando "admin".
                using var cmd = Database.Cmd(conn,
                    "SELECT Id, Senha FROM Users WHERE LOWER(User) = LOWER(@u) ORDER BY Id LIMIT 1");
                cmd.Parameters.AddWithValue("@u", u);
                using var r = cmd.ExecuteReader();
                if (!r.Read())
                {
                    MessageBox.Show("Dados incorretos.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                long userId = Convert.ToInt64(Database.FieldOrDbNull(r, "Id"));
                string stored = Database.FieldOrDbNull(r, "Senha")?.ToString() ?? "";
                r.Close();

                if (!PasswordHasher.Verify(p, stored))
                {
                    MessageBox.Show("Dados incorretos.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (PasswordHasher.NeedsUpgrade(stored))
                {
                    Database.ExecuteNonQuery(
                        "UPDATE Users SET Senha=@s WHERE Id=@id",
                        new Dictionary<string, object>
                        {
                            ["@s"] = PasswordHasher.Hash(p),
                            ["@id"] = userId
                        });
                }

                OnLoginSuccess?.Invoke(this, u);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro login: {ex.Message}");
            }
        }

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            string u = TxtRegUser.Text?.Trim() ?? "";
            string p = TxtRegPass.Password;

            if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
            {
                MessageBox.Show("Preencha usuário e senha.", "Cadastro", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var conn = Database.GetConnection())
                using (var check = Database.Cmd(conn, "SELECT 1 FROM Users WHERE LOWER(User) = LOWER(@u) LIMIT 1"))
                {
                    check.Parameters.AddWithValue("@u", u);
                    if (check.ExecuteScalar() != null)
                    {
                        MessageBox.Show(
                            $"O usuário \"{u}\" já está cadastrado.\nEscolha outro nome ou faça login.",
                            "Usuário já existe",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }

                Database.ExecuteNonQuery("INSERT INTO Users (User, Senha) VALUES (@u, @p)",
                    new Dictionary<string, object> { { "@u", u }, { "@p", PasswordHasher.Hash(p) } });

                MessageBox.Show("Usuário criado!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtRegUser.Text = "";
                TxtRegPass.Password = "";
                BtnVoltarLogin_Click(sender, e);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                MessageBox.Show(
                    $"O usuário \"{u}\" já está cadastrado.\nEscolha outro nome ou faça login.",
                    "Usuário já existe",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível cadastrar:\n{ex.Message}", "Cadastro", MessageBoxButton.OK, MessageBoxImage.Error);
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
            OnLoginSuccess?.Invoke(this, TxtLoginUser.Text);
        }

        private void BtnGoToEstoque_Click(object sender, RoutedEventArgs e)
        {
            OnEstoqueRequest?.Invoke(this, EventArgs.Empty);
        }

        private void BtnGoToAnalytics_Click(object sender, RoutedEventArgs e)
        {
            OnAnalyticsRequest?.Invoke(this, EventArgs.Empty);
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