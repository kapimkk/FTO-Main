using FTO_App.Models;
using FTO_App.Services;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FTO_App.Views
{
    public partial class ConfiguracoesView : UserControl
    {
        public ConfiguracoesView()
        {
            InitializeComponent();
            Loaded += (_, _) => Carregar();
        }

        private void Carregar()
        {
            var c = EmpresaConfigStore.Current;
            TxtNome.Text = c.Nome;
            TxtSubtitulo.Text = c.Subtitulo;
            TxtRazao.Text = c.RazaoSocial;
            TxtFantasia.Text = c.NomeFantasia;
            TxtCnpj.Text = c.Cnpj;
            TxtIe.Text = c.Ie;
            TxtIm.Text = c.Im;
            TxtCnae.Text = c.Cnae;
            TxtTelefone.Text = c.Telefone;
            TxtEmail.Text = c.Email;
            TxtEndereco.Text = c.Endereco;
            TxtNumero.Text = c.Numero;
            TxtComplemento.Text = c.Complemento;
            TxtBairro.Text = c.Bairro;
            TxtCidade.Text = c.Cidade;
            TxtUf.Text = c.Uf;
            TxtCep.Text = c.Cep;
            TxtIbge.Text = c.CodigoIbge;
            TxtSerieNfe.Text = c.SerieNfe;
            TxtUltimoNfe.Text = c.UltimoNumeroNfe;
            TxtSerieNfse.Text = c.SerieNfse;
            TxtUltimoNfse.Text = c.UltimoNumeroNfse;
            TxtFiscalUrlNfe.Text = c.FiscalApiUrlNfe;
            TxtFiscalUrlNfse.Text = c.FiscalApiUrlNfse;
            TxtFiscalApiKey.Text = c.FiscalApiKey;
            TxtNfsePTotFed.Text = c.NfsePTotTribFed.ToString("0.##");
            TxtNfsePTotEst.Text = c.NfsePTotTribEst.ToString("0.##");
            TxtNfsePTotMun.Text = c.NfsePTotTribMun.HasValue ? c.NfsePTotTribMun.Value.ToString("0.##") : "";
            ChkNfseEnviarPAliq.IsChecked = c.NfseEnviarPAliq;
            ChkNfseEnviarEndPrest.IsChecked = c.NfseEnviarEnderecoPrestador;
            TxtNfseCodTribNac.Text = EmpresaConfigStore.NormalizarCodTribNac(c.NfseCodTribNac);
            ChkNfseCodTribNacFixo.IsChecked = c.NfseCodTribNacFixo;
            if (TxtLogoPathFiscal != null) TxtLogoPathFiscal.Text = c.LogoPath;
            TxtCupomTitulo.Text = c.CupomTitulo;
            TxtCupomRodape.Text = c.CupomRodape;

            CbRegime.SelectedIndex = c.RegimeTributario switch { "2" => 1, "3" => 2, _ => 0 };
            CbAmbiente.SelectedIndex = c.AmbienteNfe == "1" ? 0 : 1;

            CbIbsCbsPreset.SelectedIndex = c.IbsCbsPreset switch
            {
                "projetado" => 1,
                "personalizado" => 2,
                _ => 0
            };
            ChkIbsCbsAuto.IsChecked = c.IbsCbsCalculoAutomatico;
            ChkIbsCbsDestaque.IsChecked = c.IbsCbsDestaqueObrigatorio;
            TxtCbsAliq.Text = c.CbsAliquota.ToString("0.####");
            TxtIbsAliq.Text = c.IbsAliquota.ToString("0.####");
            TxtIbsUfAliq.Text = c.IbsAliquotaUf.ToString("0.####");
            TxtIbsMunAliq.Text = c.IbsAliquotaMun.ToString("0.####");
            AtualizarSimulacaoIbsCbs();

            LblEnvPath.Text = "Dados da empresa e fiscais: PostgreSQL (tabela empresa_config)";
            MostrarLogo(c.LogoPath);
            LoadDevices();
            AtualizarStatusBanco();
            AtualizarStatusBackup();
        }

        private void AtualizarStatusBackup()
        {
            BackupConfig cfg = BackupService.LerConfig();

            if (string.IsNullOrWhiteSpace(cfg.Destino))
            {
                LblBackupDestino.Text =
                    $"❌ {BackupService.ChaveDestino} não configurado no .env " +
                    $"(ex.: {BackupService.ChaveDestino}=\\\\192.168.0.10\\backup-sistema-fto)";
                LblBackupDestino.Foreground = System.Windows.Media.Brushes.IndianRed;
                BtnBackupAgora.IsEnabled = false;
            }
            else
            {
                string credencial = string.IsNullOrWhiteSpace(cfg.Usuario)
                    ? "credencial do Windows"
                    : $"usuário '{cfg.Usuario}'";
                LblBackupDestino.Text = $"✅ Destino: {cfg.Destino}   ({credencial})";
                LblBackupDestino.Foreground = System.Windows.Media.Brushes.SeaGreen;
                BtnBackupAgora.IsEnabled = true;
            }

            string? pgDump = BackupService.LocalizarPgDump(cfg.PgDump);
            if (pgDump == null)
            {
                LblBackupPgDump.Text =
                    $"⚠️ pg_dump.exe não encontrado — o banco NÃO entra no backup. " +
                    $"Informe o caminho em {BackupService.ChavePgDump} no .env.";
                LblBackupPgDump.Foreground = System.Windows.Media.Brushes.DarkOrange;
            }
            else
            {
                LblBackupPgDump.Text = $"✅ pg_dump: {pgDump}";
                LblBackupPgDump.Foreground = System.Windows.Media.Brushes.SeaGreen;
            }
        }

        private async void BtnBackupAgora_Click(object sender, RoutedEventArgs e)
        {
            var botao = (Button)sender;
            string rotulo = botao.Content?.ToString() ?? "🗄️ Realizar backup do sistema";

            botao.IsEnabled = false;
            TxtBackupLog.Visibility = Visibility.Visible;
            TxtBackupLog.Text = "Iniciando backup...\r\n";

            var progresso = new Progress<string>(status =>
            {
                botao.Content = status;
                TxtBackupLog.AppendText($"{DateTime.Now:HH:mm:ss}  {status}\r\n");
                TxtBackupLog.ScrollToEnd();
            });

            try
            {
                BackupResultado r = await BackupService.ExecutarAsync(progresso);
                TxtBackupLog.AppendText("\r\n" + DescreverResultado(r));
                TxtBackupLog.ScrollToEnd();

                if (!r.Sucesso)
                {
                    MessageBox.Show(r.Erro ?? "Não foi possível concluir o backup.",
                        "Backup", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show(
                    r.Avisos.Count == 0
                        ? $"Backup concluído com sucesso.\n\n{r.PastaDestino}"
                        : $"Backup enviado, mas com {r.Avisos.Count} item(ns) de fora.\n\n" +
                          $"{r.PastaDestino}\n\nVeja o detalhe abaixo do botão e no RESUMO.txt.",
                    "Backup",
                    MessageBoxButton.OK,
                    r.Avisos.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                TxtBackupLog.AppendText($"\r\nERRO: {ex.Message}\r\n");
                MessageBox.Show($"Falha no backup.\n\n{ex.Message}",
                    "Backup", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                botao.Content = rotulo;
                botao.IsEnabled = true;
            }
        }

        private static string DescreverResultado(BackupResultado r)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("INCLUÍDO:");
            foreach (string item in r.Itens) sb.AppendLine($"  - {item}");
            if (r.Itens.Count == 0) sb.AppendLine("  (nada)");

            if (r.Avisos.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("NÃO ENTROU:");
                foreach (string aviso in r.Avisos) sb.AppendLine($"  - {aviso}");
            }

            if (!string.IsNullOrWhiteSpace(r.PastaDestino))
            {
                sb.AppendLine();
                sb.AppendLine($"Pasta: {r.PastaDestino}");
            }

            return sb.ToString();
        }

        private void BtnAbrirPastaBackup_Click(object sender, RoutedEventArgs e)
        {
            BackupConfig cfg = BackupService.LerConfig();
            if (string.IsNullOrWhiteSpace(cfg.Destino))
            {
                MessageBox.Show($"Configure {BackupService.ChaveDestino} no .env primeiro.",
                    "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var conexao = ConexaoRedeWindows.Abrir(cfg.Destino, cfg.Usuario, cfg.Senha);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = cfg.Destino,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir {cfg.Destino}.\n\n{ex.Message}",
                    "Backup", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRecarregarBackup_Click(object sender, RoutedEventArgs e)
        {
            AtualizarStatusBackup();
            MessageBox.Show("Configuração de backup relida do .env.",
                "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AtualizarStatusBanco()
        {
            try
            {
                Database.ReloadConnectionString();
                using var conn = Database.GetConnection();
                LblPgStatus.Text = "✅ Conectado ao PostgreSQL.";
                LblPgStatus.Foreground = System.Windows.Media.Brushes.SeaGreen;
            }
            catch (Exception ex)
            {
                LblPgStatus.Text = "❌ " + ex.Message;
                LblPgStatus.Foreground = System.Windows.Media.Brushes.IndianRed;
            }

            LblSqlitePath.Text = $"SQLite legado (se existir): {Database.SqliteLegacyPath}";
        }

        private void BtnTestPg_Click(object sender, RoutedEventArgs e)
        {
            AtualizarStatusBanco();
            MessageBox.Show(LblPgStatus.Text, "PostgreSQL", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnMigrarSqlite_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "⚠️ ATENÇÃO — esta operação APAGA os dados atuais do PostgreSQL.\n\n" +
                "Users, Clientes, Vendas, Produtos, Notas e Integrações são esvaziados e recopiados\n" +
                "do FTO.db (SQLite). TUDO que foi cadastrado no sistema depois da migração anterior\n" +
                "— vendas, clientes e notas novos — SERÁ PERDIDO.\n\n" +
                "Use apenas se o dashboard estiver com valores ×100 (ex.: milhões) vindos do SQLite.\n" +
                "Faça um backup do banco no pgAdmin antes de continuar.\n\n" +
                "O arquivo SQLite não será apagado.\n\nContinuar mesmo assim?",
                "Migrar SQLite → PostgreSQL",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var result = SqliteToPostgresMigrator.Migrate(truncateFirst: true);
                MessageBox.Show(result.Message, result.Success ? "Migração" : "Erro",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
                AtualizarStatusBanco();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            string codTribNac = SomenteDigitos(TxtNfseCodTribNac.Text);
            if (codTribNac.Length != 6)
            {
                MessageBox.Show(
                    "O cTribNac padrão da NFS-e deve ter exatamente 6 dígitos (ex.: 010701).",
                    "Configurações", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtNfseCodTribNac.Focus();
                return;
            }

            try
            {
                var atual = EmpresaConfigStore.Current;
                var c = new EmpresaConfig
                {
                    Nome = TxtNome.Text.Trim(),
                    Subtitulo = TxtSubtitulo.Text.Trim(),
                    RazaoSocial = TxtRazao.Text.Trim(),
                    NomeFantasia = TxtFantasia.Text.Trim(),
                    Cnpj = TxtCnpj.Text.Trim(),
                    Ie = TxtIe.Text.Trim(),
                    Im = TxtIm.Text.Trim(),
                    Cnae = TxtCnae.Text.Trim(),
                    Telefone = TxtTelefone.Text.Trim(),
                    Email = TxtEmail.Text.Trim(),
                    Endereco = TxtEndereco.Text.Trim(),
                    Numero = TxtNumero.Text.Trim(),
                    Complemento = TxtComplemento.Text.Trim(),
                    Bairro = TxtBairro.Text.Trim(),
                    Cidade = TxtCidade.Text.Trim(),
                    Uf = TxtUf.Text.Trim().ToUpperInvariant(),
                    Cep = TxtCep.Text.Trim(),
                    CodigoIbge = TxtIbge.Text.Trim(),
                    RegimeTributario = (CbRegime.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "1",
                    AmbienteNfe = (CbAmbiente.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "2",
                    SerieNfe = TxtSerieNfe.Text.Trim(),
                    UltimoNumeroNfe = TxtUltimoNfe.Text.Trim(),
                    SerieNfse = TxtSerieNfse.Text.Trim(),
                    UltimoNumeroNfse = TxtUltimoNfse.Text.Trim(),
                    FiscalApiUrlNfe = TxtFiscalUrlNfe.Text.Trim().TrimEnd('/'),
                    FiscalApiUrlNfse = TxtFiscalUrlNfse.Text.Trim().TrimEnd('/'),
                    FiscalApiKey = TxtFiscalApiKey.Text.Trim(),
                    NfsePTotTribFed = MoneyInputHelper.Parse(TxtNfsePTotFed.Text),
                    NfsePTotTribEst = MoneyInputHelper.Parse(TxtNfsePTotEst.Text),
                    NfsePTotTribMun = ParseDecimalOpcional(TxtNfsePTotMun.Text),
                    NfseEnviarPAliq = ChkNfseEnviarPAliq.IsChecked == true,
                    NfseEnviarEnderecoPrestador = ChkNfseEnviarEndPrest.IsChecked == true,
                    NfseCodTribNac = codTribNac,
                    NfseCodTribNacFixo = ChkNfseCodTribNacFixo.IsChecked == true,
                    CertificadoPath = atual.CertificadoPath,
                    LogoPath = (TxtLogoPathFiscal?.Text ?? "").Trim(),
                    CupomTitulo = TxtCupomTitulo.Text.Trim(),
                    CupomRodape = TxtCupomRodape.Text.Trim(),
                    IbsCbsPreset = (CbIbsCbsPreset.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "teste2026",
                    IbsCbsCalculoAutomatico = ChkIbsCbsAuto.IsChecked == true,
                    IbsCbsDestaqueObrigatorio = ChkIbsCbsDestaque.IsChecked == true,
                    CbsAliquota = MoneyInputHelper.Parse(TxtCbsAliq.Text),
                    IbsAliquota = MoneyInputHelper.Parse(TxtIbsAliq.Text),
                    IbsAliquotaUf = MoneyInputHelper.Parse(TxtIbsUfAliq.Text),
                    IbsAliquotaMun = MoneyInputHelper.Parse(TxtIbsMunAliq.Text)
                };

                EmpresaConfigStore.Save(c);

                if (CbImpressora.SelectedItem is string printer && !printer.StartsWith("("))
                    DeviceSettingsStore.SetPrinter(printer);
                if (CbScanner.SelectedItem is string scanner && !scanner.StartsWith("("))
                    DeviceSettingsStore.SetScanner(scanner);

                MessageBox.Show("Configurações salvas com sucesso!", "Configurações",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar: {ex.Message}", "Configurações",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string SomenteDigitos(string? texto) =>
            string.IsNullOrWhiteSpace(texto)
                ? ""
                : new string(Array.FindAll(texto.ToCharArray(), char.IsDigit));

        /// <summary>Campo opcional: vazio → null; caso contrário parseia o decimal.</summary>
        private static decimal? ParseDecimalOpcional(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            decimal v = MoneyInputHelper.Parse(texto);
            return v;
        }

        private async void BtnTestarFiscalNfe_Click(object sender, RoutedEventArgs e) =>
            await TestarConexaoFiscalAsync(TxtFiscalUrlNfe.Text.Trim(), "NF-e");

        private async void BtnTestarFiscalNfse_Click(object sender, RoutedEventArgs e) =>
            await TestarConexaoFiscalAsync(TxtFiscalUrlNfse.Text.Trim(), "NFS-e");

        private async System.Threading.Tasks.Task TestarConexaoFiscalAsync(string baseUrl, string rotulo)
        {
            LblFiscalApiStatus.Text = $"⏳ Testando conexão com a API {rotulo}...";
            LblFiscalApiStatus.Foreground = System.Windows.Media.Brushes.Gray;

            var resultado = await FiscalApiClient.TestarConexaoAsync(baseUrl);
            if (resultado.Sucesso)
            {
                LblFiscalApiStatus.Text = $"✅ API {rotulo} respondendo normalmente ({baseUrl}).";
                LblFiscalApiStatus.Foreground = System.Windows.Media.Brushes.SeaGreen;
            }
            else
            {
                LblFiscalApiStatus.Text = $"❌ Falha ao conectar na API {rotulo}: {resultado.ResumoErro()}";
                LblFiscalApiStatus.Foreground = System.Windows.Media.Brushes.IndianRed;
            }
        }

        private void CbIbsCbsPreset_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            string? tag = (CbIbsCbsPreset.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (tag == "personalizado") return;
            var (cbs, ibs, uf, mun) = ReformaTributariaService.AliquotasDoPreset(tag);
            TxtCbsAliq.Text = cbs.ToString("0.####");
            TxtIbsAliq.Text = ibs.ToString("0.####");
            TxtIbsUfAliq.Text = uf.ToString("0.####");
            TxtIbsMunAliq.Text = mun.ToString("0.####");
            AtualizarSimulacaoIbsCbs();
        }

        private void BtnSimularIbsCbs_Click(object sender, RoutedEventArgs e) => AtualizarSimulacaoIbsCbs();

        private void AtualizarSimulacaoIbsCbs()
        {
            if (LblIbsCbsSimulacao == null) return;
            var temp = new EmpresaConfig
            {
                IbsCbsPreset = (CbIbsCbsPreset.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "teste2026",
                CbsAliquota = MoneyInputHelper.Parse(TxtCbsAliq?.Text),
                IbsAliquota = MoneyInputHelper.Parse(TxtIbsAliq?.Text),
                IbsAliquotaUf = MoneyInputHelper.Parse(TxtIbsUfAliq?.Text),
                IbsAliquotaMun = MoneyInputHelper.Parse(TxtIbsMunAliq?.Text),
                RegimeTributario = (CbRegime.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "1"
            };
            var r = ReformaTributariaService.Calcular(1000m, temp);
            LblIbsCbsSimulacao.Text =
                $"CBS: {r.ValorCbs:C2} ({r.AliquotaCbs:0.####}%) · " +
                $"IBS: {r.ValorIbs:C2} ({r.AliquotaIbs:0.####}% — UF {r.ValorIbsUf:C2} / Mun {r.ValorIbsMun:C2}) · " +
                $"Total IVA: {r.ValorTotalIva:C2}\n{r.Observacao}";

            AtualizarAlertaCbsCheia(temp);
        }

        /// <summary>
        /// A partir de 2027 a CBS entra em alíquota cheia (definida por resolução do Senado).
        /// Enquanto o preset continuar no de teste, a emissão usa a referência projetada — o
        /// usuário precisa ver isso antes da virada do ano.
        /// </summary>
        private void AtualizarAlertaCbsCheia(EmpresaConfig temp)
        {
            if (LblCbsCheiaAlerta == null) return;

            int ano = DateTime.Now.Year;
            if (!ReformaTributariaService.UsandoCbsDeFallback(temp))
            {
                LblCbsCheiaAlerta.Text = "";
                return;
            }

            var (cbs2027, ufAno, munAno) = ReformaTributariaService.AliquotasOficiaisTransicao(2027, temp);
            string prefixo = ano >= 2027
                ? "⚠️ ATENÇÃO — já estamos em " + ano + ": "
                : "⚠️ Antes de 01/01/2027: ";

            LblCbsCheiaAlerta.Text = prefixo +
                "a CBS entra em alíquota cheia em 2027 e o IBS passa a " +
                $"{ufAno:0.##}% estadual + {munAno:0.##}% municipal (LC 214/2025 art. 346). " +
                "A alíquota cheia da CBS é fixada por resolução do Senado e ainda não está confirmada aqui — " +
                $"a emissão usaria a referência projetada de {cbs2027:0.##}%. " +
                "Confirme o percentual com a contabilidade e grave em \"Personalizado\".";
        }

        private void BtnLogo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.ico|Todos|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string destDir = Path.Combine(AppContext.BaseDirectory, "branding");
                Directory.CreateDirectory(destDir);
                string dest = Path.Combine(destDir, "logo" + Path.GetExtension(dlg.FileName));
                File.Copy(dlg.FileName, dest, overwrite: true);
                if (TxtLogoPathFiscal != null) TxtLogoPathFiscal.Text = dest;
                MostrarLogo(dest);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao copiar logo: {ex.Message}");
            }
        }

        private void BtnRemoverLogo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtLogoPathFiscal?.Text))
            {
                MessageBox.Show("Nenhuma logo configurada.", "Logo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show("Remover a logo do emitente?\n\nA alteração será aplicada ao salvar as configurações.",
                    "Remover logo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                string? path = TxtLogoPathFiscal?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
                    path.Contains(Path.Combine("branding", "logo"), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(path); } catch { /* arquivo pode estar em uso */ }
                }
            }
            catch { /* ignore */ }

            MostrarLogo(null);
            MessageBox.Show("Logo removida. Clique em Salvar configurações para gravar.", "Logo",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            LoadDevices();
            MessageBox.Show("Lista atualizada.", "Dispositivos", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Mostra o que o driver declara (papel, área imprimível, tamanhos disponíveis).
        /// Serve para comparar a fila USB com a de rede quando o cupom sai cortado só em uma delas.
        /// </summary>
        private void BtnDiagnosticoImpressora_Click(object sender, RoutedEventArgs e)
        {
            string? impressora = CbImpressora.SelectedItem as string
                                 ?? DeviceSettingsStore.Current.SelectedPrinter;

            TxtDiagnosticoImpressora.Text = ImpressoraDiagnostico.Gerar(impressora);
            TxtDiagnosticoImpressora.Visibility = Visibility.Visible;
        }

        private void LoadDevices()
        {
            var printers = InstalledDevicesService.GetPrinters();
            CbImpressora.ItemsSource = printers;
            if (!string.IsNullOrWhiteSpace(DeviceSettingsStore.Current.SelectedPrinter) &&
                printers.Contains(DeviceSettingsStore.Current.SelectedPrinter))
                CbImpressora.SelectedItem = DeviceSettingsStore.Current.SelectedPrinter;
            else if (printers.Count > 0)
                CbImpressora.SelectedIndex = 0;

            var scanners = InstalledDevicesService.GetScanners();
            CbScanner.ItemsSource = scanners;
            if (!string.IsNullOrWhiteSpace(DeviceSettingsStore.Current.SelectedScanner))
            {
                var match = scanners.FirstOrDefault(s =>
                    s.Equals(DeviceSettingsStore.Current.SelectedScanner, StringComparison.OrdinalIgnoreCase));
                if (match != null) CbScanner.SelectedItem = match;
            }
            else if (scanners.Count > 0)
                CbScanner.SelectedIndex = 0;
        }

        private void MostrarLogo(string? path)
        {
            try
            {
                if (TxtLogoPathFiscal != null) TxtLogoPathFiscal.Text = path ?? "";

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    if (ImgLogoFiscal != null) ImgLogoFiscal.Source = null;
                    return;
                }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                if (ImgLogoFiscal != null) ImgLogoFiscal.Source = bmp;
            }
            catch
            {
                if (ImgLogoFiscal != null) ImgLogoFiscal.Source = null;
            }
        }

        // Aba Integrações removida — cadastro de APIs externas não é mais utilizado.
    }
}
