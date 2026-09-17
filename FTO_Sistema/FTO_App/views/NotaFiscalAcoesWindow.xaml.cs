using FTO_App.Models;
using FTO_App.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FTO_App.Views
{
    /// <summary>
    /// Janela de ações fiscais de uma nota já lançada (salva no banco): gerar XML de rascunho,
    /// emitir na SEFAZ via API Fiscal, consultar status, baixar XML/DANFE, carta de correção
    /// e cancelamento.
    ///
    /// Separada do cadastro (<see cref="NotaFiscalView"/>) para que o lançamento da nota seja uma
    /// responsabilidade só (Salvar/Excluir) e a emissão/consulta/cancelamento outra — evita o
    /// cadastro ficar sobrecarregado com botões que só fazem sentido depois da nota já salva.
    /// </summary>
    public partial class NotaFiscalAcoesWindow : Window
    {
        private readonly NotaFiscalModel _nota;

        /// <summary>True quando alguma ação alterou o registro no banco (status/chave/protocolo) —
        /// sinaliza para a tela chamadora recarregar a grade.</summary>
        public bool HouveAlteracao { get; private set; }

        public NotaFiscalAcoesWindow(NotaFiscalModel nota)
        {
            InitializeComponent();
            _nota = nota ?? throw new ArgumentNullException(nameof(nota));
            _nota.Modelo = "55";

            // Ambiente não é mais escolha da janela: segue Configurações → Fiscal / NF-e enquanto
            // a nota não foi emitida; uma nota já emitida/cancelada mantém o ambiente real da SEFAZ.
            bool jaDefinida = _nota.TemChaveAcesso ||
                string.Equals(_nota.Status, "Emitida", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_nota.Status, "Cancelada", StringComparison.OrdinalIgnoreCase);
            if (!jaDefinida)
                _nota.Ambiente = FiscalApiClient.NormalizarTpAmb(EmpresaConfigStore.Current.AmbienteNfe);
            SetComboTag(CbAmbiente, string.IsNullOrWhiteSpace(_nota.Ambiente) ? "2" : _nota.Ambiente);
            CbAmbiente.SelectionChanged += (_, _) => AtualizarAvisoHorario();

            AtualizarCabecalho();
            AtualizarPainelApiFiscal();
            AtualizarAvisoHorario();
        }

        private void AtualizarCabecalho()
        {
            LblTitulo.Text = $"⚡ Ações fiscais — NF-e {_nota.NumeroExibicao}";
            LblResumo.Text = $"{_nota.DestNome}\n{_nota.ResumoItens} — {_nota.ValorTotalFormatado} · Status: {_nota.Status}";
        }

        /// <summary>O dhEmi enviado à SEFAZ é sempre o instante real do clique (ver FiscalPayloadBuilder/
        /// NfeXmlService) — nunca a data escolhida no cadastro, para respeitar a tolerância de 5 minutos.</summary>
        private void AtualizarAvisoHorario() =>
            LblAgora.Text = $"⏱ A emissão/geração de XML usa a data e hora ATUAIS ({DateTime.Now:dd/MM/yyyy HH:mm:ss}) — " +
                             "a SEFAZ rejeita notas com dhEmi desatualizado (tolerância de poucos minutos).";

        private void AtualizarPainelApiFiscal()
        {
            TxtChaveAcesso.Text = _nota.ChaveAcesso;
            TxtNProt.Text = _nota.NProt;
            TxtStatusFiscal.Text = string.IsNullOrWhiteSpace(_nota.CStat)
                ? ""
                : $"{_nota.CStat} - {(string.IsNullOrWhiteSpace(_nota.MensagemTraduzida) ? _nota.XMotivo : _nota.MensagemTraduzida)}";
        }

        private static void SetComboTag(ComboBox cb, string tag)
        {
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }
        }

        private static string GetComboTag(ComboBox? cb, string fallback)
        {
            if (cb?.SelectedItem is ComboBoxItem item && item.Tag != null)
                return item.Tag.ToString() ?? fallback;
            return fallback;
        }

        private string AmbienteAtual() => GetComboTag(CbAmbiente, "2");

        private static string BaseUrlNfe() => EmpresaConfigStore.Current.FiscalApiUrlNfe;

        private bool ValidarDadosMinimos()
        {
            _nota.GarantirItens();
            if (string.IsNullOrWhiteSpace(_nota.DestNome) || _nota.Itens.Count == 0)
            {
                MessageBox.Show("A nota não tem destinatário ou nenhum item. Edite o lançamento antes de continuar.",
                    "Nota Fiscal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var problemas = NotaFiscalValidacao.ValidarItens(_nota.Itens);
            if (problemas.Count > 0)
            {
                MessageBox.Show(
                    "Corrija os itens antes de continuar (Editar a nota):\n\n• " + string.Join("\n• ", problemas) +
                    "\n\nA SEFAZ rejeita NCM vazio com erro XSD_VALIDATION no elemento NCM.",
                    "Nota Fiscal — itens", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Concilia indIEDest × IE (tela com "9-Não contribuinte" + IE preenchida gerava XML sem IE)
            var (indIe, ie) = NfeXmlService.ConciliarIndIeDest(_nota.IndIEDest, _nota.DestIe);
            _nota.IndIEDest = indIe;
            _nota.DestIe = ie;
            string? erroIe = NfeXmlService.ValidarIndIeDest(indIe, ie);
            if (erroIe != null)
            {
                MessageBox.Show(erroIe, "Nota Fiscal — IE do destinatário", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var cfg = EmpresaConfigStore.Current;
            if (string.IsNullOrWhiteSpace(cfg.CodigoIbge) || string.IsNullOrWhiteSpace(cfg.Cnpj))
            {
                MessageBox.Show("Complete CNPJ e código IBGE da empresa em Configurações antes de continuar.", "Nota Fiscal",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private bool ExigirChaveDeAcesso()
        {
            if (!string.IsNullOrWhiteSpace(TxtChaveAcesso?.Text)) return true;
            MessageBox.Show("Emita a nota na API Fiscal primeiro (botão \"EMITIR NA SEFAZ\") para obter a chave de acesso.",
                "Nota Fiscal", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void SalvarResultadoEmissao()
        {
            Database.ExecuteNonQuery(@"UPDATE NotasFiscais SET ChaveAcesso=@ca, NProt=@np, DhRecbto=@dh,
                CStat=@cs, XMotivo=@xm, MensagemTraduzida=@mt, QrCodeUrl=@qr, XmlAutorizado=@xa, Status=@st
                WHERE Id=@id",
                new Dictionary<string, object>
                {
                    ["@ca"] = _nota.ChaveAcesso, ["@np"] = _nota.NProt, ["@dh"] = _nota.DhRecbto,
                    ["@cs"] = _nota.CStat, ["@xm"] = _nota.XMotivo, ["@mt"] = _nota.MensagemTraduzida,
                    ["@qr"] = _nota.QrCodeUrl, ["@xa"] = _nota.XmlAutorizado, ["@st"] = _nota.Status,
                    ["@id"] = _nota.Id
                });
            HouveAlteracao = true;
        }

        private void BtnGerarXml_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidarDadosMinimos()) return;
            try
            {
                _nota.Ambiente = AmbienteAtual();
                _nota.Modelo = "55";
                string path = NfeXmlService.SalvarXml(_nota, EmpresaConfigStore.Current);
                _nota.CaminhoXml = path;
                _nota.Status = "XML gerado";

                Database.ExecuteNonQuery(
                    "UPDATE NotasFiscais SET CaminhoXml=@x, Status=@s, Ambiente=@a WHERE Id=@id",
                    new Dictionary<string, object> { ["@x"] = path, ["@s"] = "XML gerado", ["@a"] = _nota.Ambiente, ["@id"] = _nota.Id });
                HouveAlteracao = true;

                MessageBox.Show($"XML gerado:\n{path}", "Nota Fiscal", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao gerar XML: {ex.Message}", "Nota Fiscal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnEmitirApi_Click(object sender, RoutedEventArgs e)
        {
            if (string.Equals(_nota.Status, "Emitida", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Esta NF-e já está Emitida. Não reemita o mesmo número — a SEFAZ rejeita com 539 (duplicidade).\n\n" +
                    "Para nova venda, cadastre outra nota (próximo número).",
                    "Emissão", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.Equals(_nota.Status, "Cancelada", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Nota cancelada não pode ser reemitida com o mesmo número. Cadastre uma nova NF-e.",
                    "Emissão", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!ValidarDadosMinimos()) return;

            var cfg = EmpresaConfigStore.Current;
            _nota.Ambiente = FiscalApiClient.NormalizarTpAmb(AmbienteAtual());
            _nota.Modelo = "55";

            // Normaliza campos que a SEFAZ/XSD rejeitam se vierem malformados do rascunho — item a item
            foreach (var item in _nota.Itens)
            {
                item.Ncm = ReformaTributariaService.NormalizarNcm(item.Ncm);
                item.ClassTrib = ReformaTributariaService.NormalizarClassTrib(item.ClassTrib);
                item.CstIbsCbs = ReformaTributariaService.NormalizarCst(item.CstIbsCbs);
                item.Recalcular();

                var ibsCbs = ReformaTributariaService.CalcularParaEmissao(item);
                item.CbsAliquota = ibsCbs.AliquotaCbs;
                item.CbsValor = ibsCbs.ValorCbs;
                item.IbsAliquota = ibsCbs.AliquotaIbs;
                item.IbsValor = ibsCbs.ValorIbs;
                item.IbsAliquotaUf = ibsCbs.AliquotaIbsUf;
                item.IbsValorUf = ibsCbs.ValorIbsUf;
                item.IbsAliquotaMun = ibsCbs.AliquotaIbsMun;
                item.IbsValorMun = ibsCbs.ValorIbsMun;
            }
            _nota.RecalcularTotais();

            string baseUrl = BaseUrlNfe();
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(cfg.FiscalApiKey))
            {
                MessageBox.Show("Configure a URL da API Fiscal e a API Key em Configurações → Fiscal / NF-e antes de emitir.",
                    "Nota Fiscal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnEmitirApi.IsEnabled = false;
            TxtStatusFiscal.Text = "⏳ Emitindo na SEFAZ...";
            try
            {
                // Persiste o ambiente escolhido na janela (homolog/produção) antes do POST
                Database.ExecuteNonQuery(
                    "UPDATE NotasFiscais SET Ambiente=@a WHERE Id=@id",
                    new Dictionary<string, object> { ["@a"] = _nota.Ambiente, ["@id"] = _nota.Id });
                HouveAlteracao = true;

                var resultado = await FiscalApiClient.EmitirAsync(_nota, cfg, baseUrl, cfg.FiscalApiKey);

                if (!resultado.Sucesso)
                {
                    TxtStatusFiscal.Text = "";
                    MessageBox.Show($"Falha ao emitir a nota:\n\n{resultado.ResumoErro()}", "Emissão — Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var dados = resultado.Dados!;
                _nota.ChaveAcesso = dados.ChaveAcesso ?? "";
                _nota.NProt = dados.NProt ?? "";
                _nota.DhRecbto = dados.DhRecbto ?? "";
                _nota.CStat = dados.CStat ?? "";
                _nota.XMotivo = dados.XMotivo ?? "";
                _nota.MensagemTraduzida = dados.MensagemTraduzida ?? "";
                _nota.XmlAutorizado = dados.XmlAutorizado ?? "";
                _nota.QrCodeUrl = dados.QrCodeUrl ?? "";
                _nota.Status = dados.Aprovado ? "Emitida" : "Rejeitada";

                SalvarResultadoEmissao();
                AtualizarPainelApiFiscal();
                AtualizarCabecalho();

                // Só produção move o contador oficial — emissão de teste não pode abrir buraco
                // na numeração (que depois exigiria inutilização da faixa).
                if (dados.Aprovado && FiscalApiClient.NormalizarTpAmb(_nota.Ambiente) == "1")
                    EmpresaConfigStore.AtualizarUltimoNumeroNfeSeMaior(_nota.Numero);

                string resumoProblemas = dados.Problemas is { Count: > 0 }
                    ? "\n\nProblemas reportados:\n" + string.Join("\n", dados.Problemas.ConvertAll(p => $"- [{p.Codigo}] {p.Mensagem}"))
                    : "";

                if (dados.Aprovado)
                {
                    MessageBox.Show(
                        $"✅ Nota autorizada com sucesso!\n\nChave de acesso: {_nota.ChaveAcesso}\nProtocolo: {_nota.NProt}\nRecebimento: {_nota.DhRecbto}\n\n{dados.CStat} - {dados.XMotivo}",
                        "Emissão — Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    string extra539 = "";
                    if (EhDuplicidadeNf(_nota.CStat, _nota.XMotivo, _nota.MensagemTraduzida, dados.Erro))
                    {
                        extra539 =
                            "\n\nO que fazer:\n" +
                            $"• A série {_nota.Serie} nº {_nota.Numero} já existe na SEFAZ para este CNPJ (não reutilize).\n" +
                            "• Em Configurações → Fiscal, ajuste \"Último nº NF-e\" para o último número realmente usado em produção.\n" +
                            "• Cadastre uma NOVA nota (próximo número) e emita de novo.\n" +
                            "• Se a nota antiga for a que você quer, use Consultar status / Baixar XML com a chave da SEFAZ.";
                    }

                    MessageBox.Show(
                        $"⚠️ A nota NÃO foi autorizada pela SEFAZ.\n\n{dados.CStat} - {(dados.MensagemTraduzida ?? dados.XMotivo)}{resumoProblemas}\n\n{dados.Erro}{extra539}",
                        "Emissão — Rejeitada", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro inesperado ao emitir: {ex.Message}", "Nota Fiscal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnEmitirApi.IsEnabled = true;
            }
        }

        private static bool EhDuplicidadeNf(string? cStat, string? xMotivo, string? traduzida, string? erro)
        {
            if (cStat == "539") return true;
            string blob = $"{xMotivo} {traduzida} {erro}";
            return blob.Contains("Duplicidade", StringComparison.OrdinalIgnoreCase)
                   || blob.Contains("já existe NF-e", StringComparison.OrdinalIgnoreCase);
        }

        private async void BtnConsultarStatus_Click(object sender, RoutedEventArgs e)
        {
            if (!ExigirChaveDeAcesso()) return;
            string chave = TxtChaveAcesso.Text.Trim();
            var cfg = EmpresaConfigStore.Current;

            var resultado = await FiscalApiClient.ConsultarStatusAsync(BaseUrlNfe(), cfg.FiscalApiKey, chave, AmbienteAtual());
            if (!resultado.Sucesso)
            {
                MessageBox.Show($"Falha ao consultar status:\n\n{resultado.ResumoErro()}", "Consulta de status",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dados = resultado.Dados!;
            _nota.CStat = dados.CStat ?? "";
            _nota.XMotivo = dados.XMotivo ?? "";
            if (!string.IsNullOrWhiteSpace(dados.NProt)) _nota.NProt = dados.NProt;
            TxtStatusFiscal.Text = $"{dados.SituacaoDescricao} — {dados.CStat} - {dados.XMotivo}";
            TxtNProt.Text = _nota.NProt;

            Database.ExecuteNonQuery(
                "UPDATE NotasFiscais SET CStat=@c, XMotivo=@x, NProt=@n WHERE Id=@id",
                new Dictionary<string, object>
                {
                    ["@c"] = _nota.CStat, ["@x"] = _nota.XMotivo, ["@n"] = _nota.NProt, ["@id"] = _nota.Id
                });
            HouveAlteracao = true;

            MessageBox.Show($"Situação: {dados.SituacaoDescricao}\n{dados.CStat} - {dados.XMotivo}\nProtocolo: {dados.NProt}",
                "Consulta de status", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnBaixarXml_Click(object sender, RoutedEventArgs e)
        {
            if (!ExigirChaveDeAcesso()) return;
            string chave = TxtChaveAcesso.Text.Trim();
            var cfg = EmpresaConfigStore.Current;

            var resultado = await FiscalApiClient.ObterXmlAsync(BaseUrlNfe(), cfg.FiscalApiKey, chave, AmbienteAtual());
            if (!resultado.Sucesso)
            {
                MessageBox.Show($"Falha ao baixar o XML:\n\n{resultado.ResumoErro()}", "Download de XML",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "XML|*.xml", FileName = $"NFe_{chave}.xml" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                System.IO.File.WriteAllText(dlg.FileName, resultado.Dados, new System.Text.UTF8Encoding(false));
                MessageBox.Show("XML salvo com sucesso!", "Download de XML", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar o arquivo: {ex.Message}", "Download de XML", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnVerXml_Click(object sender, RoutedEventArgs e)
        {
            if (!ExigirChaveDeAcesso()) return;
            string chave = TxtChaveAcesso.Text.Trim();
            var cfg = EmpresaConfigStore.Current;

            var resultado = await FiscalApiClient.ObterXmlAsync(BaseUrlNfe(), cfg.FiscalApiKey, chave, AmbienteAtual());
            if (!resultado.Sucesso)
            {
                MessageBox.Show($"Falha ao obter o XML:\n\n{resultado.ResumoErro()}", "Visualizar XML",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var viewer = new XmlViewerWindow($"👁 XML da nota — chave {chave}", resultado.Dados!, $"NFe_{chave}.xml")
            {
                Owner = this
            };
            viewer.ShowDialog();
        }

        private async void BtnBaixarDanfe_Click(object sender, RoutedEventArgs e)
        {
            if (!ExigirChaveDeAcesso()) return;
            string chave = TxtChaveAcesso.Text.Trim();
            var cfg = EmpresaConfigStore.Current;
            bool temLogo = !string.IsNullOrWhiteSpace(cfg.LogoPath) && System.IO.File.Exists(cfg.LogoPath);

            // NF-e com logo: gera PDF A4 local com a logo do emitente.
            if (temLogo)
            {
                var dlgLogo = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PDF|*.pdf",
                    DefaultExt = ".pdf",
                    FileName = DocumentoArquivoNome.Montar(
                        DocumentoArquivoNome.PrefixoNfe, _nota.DestNome, _nota.DataEmissao, ".pdf")
                };
                if (dlgLogo.ShowDialog() != true) return;

                try
                {
                    PdfService.GerarDanfeNfeComLogo(_nota, cfg, dlgLogo.FileName);

                    bool salvouOficial = false;
                    var oficial = await FiscalApiClient.ObterDanfeAsync(
                        BaseUrlNfe(), cfg.FiscalApiKey, chave, AmbienteAtual());
                    if (oficial.Sucesso && oficial.Dados is { Length: > 0 })
                    {
                        string pathOficial = System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(dlgLogo.FileName)!,
                            DocumentoArquivoNome.Montar(
                                DocumentoArquivoNome.PrefixoNfe, _nota.DestNome, _nota.DataEmissao, ".pdf", "oficial"));
                        System.IO.File.WriteAllBytes(pathOficial, oficial.Dados);
                        salvouOficial = true;
                    }

                    if (MessageBox.Show(
                            "DANFE da NF-e com logo salva com sucesso!" +
                            (salvouOficial ? "\nA DANFE oficial da API também foi salva ao lado." : "") +
                            "\n\nDeseja abrir a versão com logo agora?",
                            "Download de DANFE", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlgLogo.FileName)
                        { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao gerar DANFE com logo: {ex.Message}", "Download de DANFE",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            var resultado = await FiscalApiClient.ObterDanfeAsync(BaseUrlNfe(), cfg.FiscalApiKey, chave, AmbienteAtual());
            if (!resultado.Sucesso)
            {
                MessageBox.Show($"Falha ao baixar a DANFE:\n\n{resultado.ResumoErro()}", "Download de DANFE",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF|*.pdf",
                DefaultExt = ".pdf",
                FileName = DocumentoArquivoNome.Montar(
                    DocumentoArquivoNome.PrefixoNfe, _nota.DestNome, _nota.DataEmissao, ".pdf")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                System.IO.File.WriteAllBytes(dlg.FileName, resultado.Dados!);
                if (MessageBox.Show("DANFE salva com sucesso! Deseja abrir agora?", "Download de DANFE",
                        MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar o arquivo: {ex.Message}", "Download de DANFE", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnCartaCorrecao_Click(object sender, RoutedEventArgs e)
        {
            if (!ExigirChaveDeAcesso()) return;

            var win = new CartaCorrecaoWindow { Owner = this };
            if (win.ShowDialog() != true) return;

            var cfg = EmpresaConfigStore.Current;
            string chave = TxtChaveAcesso.Text.Trim();

            var resultado = await FiscalApiClient.CartaCorrecaoAsync(
                BaseUrlNfe(), cfg.FiscalApiKey, chave, cfg.Cnpj, win.Correcao, win.Sequencial, AmbienteAtual());

            if (!resultado.Sucesso)
            {
                MessageBox.Show($"Falha ao registrar a CC-e:\n\n{resultado.ResumoErro()}", "Carta de Correção",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dados = resultado.Dados!;
            MessageBox.Show(dados.Aprovado
                    ? $"✅ CC-e registrada com sucesso!\n\nProtocolo: {dados.NProt}\n{dados.CStat} - {dados.XMotivo}"
                    : $"⚠️ CC-e não foi aceita pela SEFAZ.\n\n{dados.CStat} - {(dados.MensagemTraduzida ?? dados.XMotivo)}\n\n{dados.Erro}",
                "Carta de Correção", MessageBoxButton.OK, dados.Aprovado ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private async void BtnCancelarNota_Click(object sender, RoutedEventArgs e)
        {
            if (!ExigirChaveDeAcesso()) return;
            if (string.IsNullOrWhiteSpace(TxtNProt.Text))
            {
                MessageBox.Show("Não é possível cancelar: esta nota não possui protocolo de autorização registrado.",
                    "Cancelamento", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string chave = TxtChaveAcesso.Text.Trim();
            string resumo = $"Nota {_nota.NumeroExibicao} — {_nota.DestNome}\nChave: {chave}\nProtocolo: {TxtNProt.Text}";
            var win = new CancelamentoWindow(resumo) { Owner = this };
            if (win.ShowDialog() != true) return;

            var cfg = EmpresaConfigStore.Current;

            // Garante dhEvento >= emissão/autorização (evita rejeição SEFAZ 577 por fuso/UTC no servidor da API).
            DateTimeOffset dhEvento = CalcularDhEventoCancelamento(_nota);

            var resultado = await FiscalApiClient.CancelarAsync(
                BaseUrlNfe(), cfg.FiscalApiKey, chave, cfg.Cnpj, TxtNProt.Text.Trim(),
                win.Justificativa, AmbienteAtual(), dhEvento);

            if (!resultado.Sucesso)
            {
                MessageBox.Show($"Falha ao cancelar a nota:\n\n{resultado.ResumoErro()}", "Cancelamento",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dados = resultado.Dados!;
            if (dados.Aprovado)
            {
                _nota.Status = "Cancelada";
                _nota.CStat = dados.CStat ?? "";
                _nota.XMotivo = dados.XMotivo ?? "";
                Database.ExecuteNonQuery("UPDATE NotasFiscais SET Status=@s, CStat=@c, XMotivo=@x WHERE Id=@id",
                    new Dictionary<string, object>
                    {
                        ["@s"] = "Cancelada", ["@c"] = _nota.CStat, ["@x"] = _nota.XMotivo, ["@id"] = _nota.Id
                    });
                HouveAlteracao = true;
                TxtStatusFiscal.Text = $"Cancelada — {dados.CStat} - {dados.XMotivo}";
                AtualizarCabecalho();
            }

            MessageBox.Show(dados.Aprovado
                    ? $"✅ Nota cancelada com sucesso!\n\n{dados.CStat} - {dados.XMotivo}"
                    : $"⚠️ Cancelamento não foi aceito pela SEFAZ.\n\n{dados.CStat} - {(dados.MensagemTraduzida ?? dados.XMotivo)}\n\n{dados.Erro}",
                "Cancelamento", MessageBoxButton.OK, dados.Aprovado ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        /// <summary>
        /// dhEvento com fuso local, nunca anterior à emissão/autorização da nota (SEFAZ 577).
        /// </summary>
        private static DateTimeOffset CalcularDhEventoCancelamento(NotaFiscalModel nota)
        {
            DateTimeOffset agora = DateTimeOffset.Now;
            DateTimeOffset minimo = agora;

            // DataEmissao do cadastro (pode ser só data)
            if (nota.DataEmissao > DateTime.MinValue)
            {
                var emi = new DateTimeOffset(DateTime.SpecifyKind(nota.DataEmissao, DateTimeKind.Local));
                if (emi > minimo) minimo = emi;
            }

            // Protocolo / recebimento SEFAZ, se disponível
            if (!string.IsNullOrWhiteSpace(nota.DhRecbto) &&
                DateTimeOffset.TryParse(nota.DhRecbto, out var dhRec) &&
                dhRec > minimo)
                minimo = dhRec;

            // dhEmi do XML autorizado (fonte da verdade na SEFAZ)
            string? dhEmiXml = ExtrairDhEmiDoXml(nota.XmlAutorizado);
            if (!string.IsNullOrWhiteSpace(dhEmiXml) &&
                DateTimeOffset.TryParse(dhEmiXml, out var dhEmi) &&
                dhEmi > minimo)
                minimo = dhEmi;

            // Evento deve ser estritamente posterior à emissão
            if (agora <= minimo)
                return minimo.AddSeconds(5);
            return agora;
        }

        private static string? ExtrairDhEmiDoXml(string? xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    xml, @"<dhEmi[^>]*>(?<v>[^<]+)</dhEmi>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return m.Success ? m.Groups["v"].Value.Trim() : null;
            }
            catch { return null; }
        }

        private void BtnFechar_Click(object sender, RoutedEventArgs e) => Close();
    }
}
