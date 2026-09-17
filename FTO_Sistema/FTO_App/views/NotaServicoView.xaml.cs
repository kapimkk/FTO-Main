using FTO_App.Models;
using FTO_App.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FTO_App.Views
{
    public partial class NotaServicoView : UserControl
    {
        private const int PageSize = 20;
        private int _page = 1;
        private int _totalPages = 1;
        private string _filtro = "";
        private long? _editingId;
        private string _statusAoEditar = "Rascunho";
        private bool _buscandoCep;
        private readonly List<ClienteModel> _clientes = new();

        private static bool StatusBloqueiaEdicao(string? status) =>
            string.Equals(status, "Emitida", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Cancelada", StringComparison.OrdinalIgnoreCase);

        public NotaServicoView()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                LoadClientes();
                LoadGrid();
            };
        }

        private void TxtBusca_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filtro = TxtBusca.Text?.Trim() ?? "";
            _page = 1;
            LoadGrid();
        }

        private void Filtro_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _page = 1;
            LoadGrid();
        }

        private void BtnFiltrar_Click(object sender, RoutedEventArgs e)
        {
            _page = 1;
            LoadGrid();
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_page > 1) { _page--; LoadGrid(); }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_page < _totalPages) { _page++; LoadGrid(); }
        }

        private void BtnNovo_Click(object sender, RoutedEventArgs e)
        {
            _editingId = null;
            _statusAoEditar = "Rascunho";
            LimparForm();
            PreencherDefaults();
            LblFormTitulo.Text = "Cadastrar NFS-e";
            FormOverlay.Visibility = Visibility.Visible;
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if (GridNotas.SelectedItem is not NotaServicoModel sel)
            {
                MessageBox.Show("Selecione uma NFS-e na lista.", "NFS-e", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var full = CarregarPorId(sel.Id);
            if (full == null) return;
            if (StatusBloqueiaEdicao(full.Status))
            {
                MessageBox.Show(
                    $"NFS-e com status \"{full.Status}\" não pode ser editada.\nUse Ações fiscais para consultar/cancelar.",
                    "NFS-e", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _editingId = full.Id;
            _statusAoEditar = string.IsNullOrWhiteSpace(full.Status) ? "Rascunho" : full.Status;
            PreencherForm(full);
            LblFormTitulo.Text = "Editar NFS-e";
            FormOverlay.Visibility = Visibility.Visible;
        }

        private void BtnAcoes_Click(object sender, RoutedEventArgs e)
        {
            if (GridNotas.SelectedItem is not NotaServicoModel sel)
            {
                MessageBox.Show("Selecione uma NFS-e na lista.", "NFS-e", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var full = CarregarPorId(sel.Id);
            if (full == null) return;

            var win = new NotaServicoAcoesWindow(full) { Owner = Window.GetWindow(this) };
            win.ShowDialog();
            if (win.HouveAlteracao) LoadGrid();
        }

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => BtnEditar_Click(sender, e);

        private void BtnExcluir_Click(object sender, RoutedEventArgs e)
        {
            if (GridNotas.SelectedItem is not NotaServicoModel sel) return;
            if (string.Equals(sel.Status, "Emitida", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sel.Status, "Cancelada", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Não é possível excluir NFS-e emitida ou cancelada.",
                    "NFS-e", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (MessageBox.Show($"Excluir NFS-e {sel.NumeroExibicao}?", "Excluir",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            Database.ExecuteNonQuery("DELETE FROM notasservico WHERE id=@id",
                new Dictionary<string, object> { ["@id"] = sel.Id });
            LoadGrid();
        }

        private void BtnFecharForm_Click(object sender, RoutedEventArgs e) =>
            FormOverlay.Visibility = Visibility.Collapsed;

        /// <summary>Selo no cabeçalho do formulário: deixa claro, sem abrir o combo, onde a DPS vai ser transmitida.</summary>
        private void CbAmbienteNfse_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LblBadgeAmbienteNfse == null || BadgeAmbienteNfse == null) return;
            bool homolog = (CbAmbiente.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "1";

            LblBadgeAmbienteNfse.Text = homolog ? "HOMOLOGAÇÃO" : "PRODUÇÃO";
            BadgeAmbienteNfse.Background = new System.Windows.Media.SolidColorBrush(homolog
                ? System.Windows.Media.Color.FromRgb(0xFE, 0xF3, 0xC7)
                : System.Windows.Media.Color.FromRgb(0xDC, 0xFC, 0xE7));
            LblBadgeAmbienteNfse.Foreground = new System.Windows.Media.SolidColorBrush(homolog
                ? System.Windows.Media.Color.FromRgb(0xB4, 0x53, 0x09)
                : System.Windows.Media.Color.FromRgb(0x15, 0x80, 0x3D));
        }

        private void TxtValorServico_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (LblResumoValorNfse == null) return;
            LblResumoValorNfse.Text = MoneyInputHelper.Parse(TxtValorServico.Text)
                .ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        }

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var nota = MontarDoForm();
                if (!ValidarCadastro(nota)) return;

                var p = Params(nota);
                if (_editingId.HasValue)
                {
                    p["@id"] = _editingId.Value;
                    // Não altera status/chave/XMLs — só o cadastro da DPS
                    Database.ExecuteNonQuery(@"
                        UPDATE notasservico SET
                            ambiente=@amb, serie=@ser, numerodps=@num, datacompetencia=@dc, codigoibgeemissao=@ibgee,
                            tomadornome=@tn, tomadorcpfcnpj=@td, tomadoremail=@te, tomadorfone=@tf,
                            tomadorlgr=@tl, tomadornro=@tnr, tomadorbairro=@tb, tomadormun=@tm, tomadoruf=@tuf,
                            tomadorcep=@tcep, tomadoribge=@tib, codtribnac=@ctn, codtribmun=@ctm,
                            descricaoservico=@ds, codnbs=@nbs, codibgeprestacao=@ibgep, valorservico=@vs,
                            tribissqn=@ti, tpretissqn=@tr, aliquotaiss=@ai, opsimpnac=@op, regaptribsn=@ras, regesptrib=@re,
                            incluiribscbs=@ibs, cstibscbs=@cst, classtrib=@cl, codindop=@cio
                        WHERE id=@id", p);
                }
                else
                {
                    _editingId = Database.ExecuteInsertReturnId(@"
                        INSERT INTO notasservico
                            (ambiente,serie,numerodps,datacompetencia,codigoibgeemissao,
                             tomadornome,tomadorcpfcnpj,tomadoremail,tomadorfone,
                             tomadorlgr,tomadornro,tomadorbairro,tomadormun,tomadoruf,tomadorcep,tomadoribge,
                             codtribnac,codtribmun,descricaoservico,codnbs,codibgeprestacao,valorservico,
                             tribissqn,tpretissqn,aliquotaiss,opsimpnac,regaptribsn,regesptrib,
                             incluiribscbs,cstibscbs,classtrib,codindop,status)
                        VALUES
                            (@amb,@ser,@num,@dc,@ibgee,@tn,@td,@te,@tf,@tl,@tnr,@tb,@tm,@tuf,@tcep,@tib,
                             @ctn,@ctm,@ds,@nbs,@ibgep,@vs,@ti,@tr,@ai,@op,@ras,@re,@ibs,@cst,@cl,@cio,@st)", p);
                }

                FormOverlay.Visibility = Visibility.Collapsed;
                LoadGrid();
                MessageBox.Show("NFS-e salva. Use o botão Emitir NFS-e para transmitir ao SEFIN.", "NFS-e",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar: {ex.Message}", "NFS-e", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool ValidarCadastro(NotaServicoModel n)
        {
            if (string.IsNullOrWhiteSpace(n.Serie) || n.NumeroDps <= 0)
            {
                MessageBox.Show("Série e número da DPS são obrigatórios.", "NFS-e", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            string doc = Digitos(n.TomadorCpfCnpj);
            if (doc.Length is not (11 or 14) || string.IsNullOrWhiteSpace(n.TomadorNome))
            {
                MessageBox.Show("Tomador: nome e CPF (11) ou CNPJ (14) obrigatórios.", "NFS-e",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (Digitos(n.CodTribNac).Length != 6)
            {
                MessageBox.Show("cTribNac deve ter 6 dígitos (ex.: 010101).", "NFS-e",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(n.DescricaoServico) || n.ValorServico <= 0)
            {
                MessageBox.Show("Descrição e valor do serviço são obrigatórios.", "NFS-e",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (Digitos(n.CodigoIbgeEmissao).Length != 7 || Digitos(n.CodIbgePrestacao).Length != 7)
            {
                MessageBox.Show("Códigos IBGE de emissão e prestação devem ter 7 dígitos.", "NFS-e",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (n.IncluirIbsCbs && Digitos(n.CodNbs).Length != 9)
            {
                MessageBox.Show("Com IBS/CBS, informe NBS com 9 dígitos.", "NFS-e",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private NotaServicoModel MontarDoForm()
        {
            long.TryParse(TxtNumeroDps.Text?.Trim(), out long num);
            return new NotaServicoModel
            {
                Id = _editingId ?? 0,
                Ambiente = GetComboTag(CbAmbiente, "2"),
                Serie = TxtSerie.Text.Trim(),
                NumeroDps = num,
                DataCompetencia = DpCompetencia.SelectedDate ?? DateTime.Today,
                CodigoIbgeEmissao = Digitos(TxtIbgeEmissao.Text),
                TomadorNome = TxtTomadorNome.Text.Trim(),
                TomadorCpfCnpj = Digitos(TxtTomadorDoc.Text),
                TomadorEmail = TxtTomadorEmail.Text.Trim(),
                TomadorFone = Digitos(TxtTomadorFone.Text),
                TomadorLgr = TxtTomadorLgr.Text.Trim(),
                TomadorNro = TxtTomadorNro.Text.Trim(),
                TomadorBairro = TxtTomadorBairro.Text.Trim(),
                TomadorMun = TxtTomadorMun.Text.Trim(),
                TomadorUf = TxtTomadorUf.Text.Trim().ToUpperInvariant(),
                TomadorCep = Digitos(TxtTomadorCep.Text),
                TomadorIbge = Digitos(TxtTomadorIbge.Text),
                // Com o código fixado em Configurações, a nota sempre usa o valor configurado
                CodTribNac = EmpresaConfigStore.Current.NfseCodTribNacFixo
                    ? EmpresaConfigStore.NormalizarCodTribNac(EmpresaConfigStore.Current.NfseCodTribNac)
                    : Digitos(TxtCodTribNac.Text),
                CodTribMun = Digitos(TxtCodTribMun.Text),
                DescricaoServico = TxtDescServico.Text.Trim(),
                CodNbs = Digitos(TxtCodNbs.Text),
                CodIbgePrestacao = Digitos(TxtIbgePrestacao.Text),
                ValorServico = MoneyInputHelper.Parse(TxtValorServico.Text),
                TribIssqn = GetComboTag(CbTribIssqn, "1"),
                TpRetIssqn = GetComboTag(CbTpRetIssqn, "1"),
                AliquotaIss = MoneyInputHelper.Parse(TxtAliqIss.Text),
                OpSimpNac = GetComboTag(CbOpSimpNac, "1"),
                RegApTribSN = GetComboTag(CbRegApTribSN, "1"),
                RegEspTrib = string.IsNullOrWhiteSpace(TxtRegEspTrib.Text) ? "0" : TxtRegEspTrib.Text.Trim(),
                IncluirIbsCbs = ChkIbsCbs.IsChecked == true,
                CstIbsCbs = Digitos(TxtCstIbsCbs.Text),
                ClassTrib = Digitos(TxtClassTrib.Text),
                CodIndOp = Digitos(TxtCodIndOp.Text),
                // INSERT: Rascunho. UPDATE não grava status (preserva Emitida/etc.).
                Status = _editingId.HasValue ? _statusAoEditar : "Rascunho"
            };
        }

        private static Dictionary<string, object> Params(NotaServicoModel n) => new()
        {
            ["@amb"] = n.Ambiente,
            ["@ser"] = n.Serie,
            ["@num"] = n.NumeroDps,
            // Coluna é DATE — DateOnly evita o cast implícito de texto/timestamp
            ["@dc"] = DateOnly.FromDateTime(n.DataCompetencia),
            ["@ibgee"] = n.CodigoIbgeEmissao,
            ["@tn"] = n.TomadorNome,
            ["@td"] = n.TomadorCpfCnpj,
            ["@te"] = n.TomadorEmail,
            ["@tf"] = n.TomadorFone,
            ["@tl"] = n.TomadorLgr,
            ["@tnr"] = n.TomadorNro,
            ["@tb"] = n.TomadorBairro,
            ["@tm"] = n.TomadorMun,
            ["@tuf"] = n.TomadorUf,
            ["@tcep"] = n.TomadorCep,
            ["@tib"] = n.TomadorIbge,
            ["@ctn"] = n.CodTribNac,
            ["@ctm"] = n.CodTribMun,
            ["@ds"] = n.DescricaoServico,
            ["@nbs"] = n.CodNbs,
            ["@ibgep"] = n.CodIbgePrestacao,
            ["@vs"] = n.ValorServico,
            ["@ti"] = n.TribIssqn,
            ["@tr"] = n.TpRetIssqn,
            ["@ai"] = n.AliquotaIss,
            ["@op"] = n.OpSimpNac,
            ["@ras"] = string.IsNullOrWhiteSpace(n.RegApTribSN) ? "1" : n.RegApTribSN,
            ["@re"] = n.RegEspTrib,
            ["@ibs"] = n.IncluirIbsCbs ? 1 : 0,
            ["@cst"] = n.CstIbsCbs,
            ["@cl"] = n.ClassTrib,
            ["@cio"] = n.CodIndOp,
            ["@st"] = n.Status
        };

        private void PreencherDefaults()
        {
            var cfg = EmpresaConfigStore.Current;
            // NFS-e NÃO herda AmbienteNfe: a API Fiscal NFS-e padrão aponta para
            // sefin.producaorestrita (sandbox), que exige tpAmb=2. Herdar "1" da NF-e causa E0006.
            SetComboTag(CbAmbiente, "2");
            TxtSerie.Text = string.IsNullOrWhiteSpace(cfg.SerieNfse) ? "1" : cfg.SerieNfse;
            TxtNumeroDps.Text = ProximoNumero().ToString();
            DpCompetencia.SelectedDate = DateTime.Today;
            TxtIbgeEmissao.Text = Digitos(cfg.CodigoIbge);
            TxtIbgePrestacao.Text = Digitos(cfg.CodigoIbge);
            TxtCodTribNac.Text = EmpresaConfigStore.NormalizarCodTribNac(cfg.NfseCodTribNac);
            AplicarTravaCodTribNac();
            TxtAliqIss.Text = "5";
            TxtRegEspTrib.Text = "0";
            TxtCstIbsCbs.Text = "000";
            TxtClassTrib.Text = "000001";
            TxtCodIndOp.Text = "000001";
            ChkIbsCbs.IsChecked = false;

            // CRT empresa → sugestão opSimpNac + regApTribSN
            string op = (cfg.RegimeTributario ?? "1") switch
            {
                "1" or "2" => "3",
                _ => "1"
            };
            SetComboTag(CbOpSimpNac, op);
            // CRT 2 (excesso) sugere ISS fora do SN; CRT 1 (dentro do SN) usa apuração pelo SN
            SetComboTag(CbRegApTribSN, (cfg.RegimeTributario ?? "1") == "2" ? "2" : "1");
            SetComboTag(CbTribIssqn, "1");
            SetComboTag(CbTpRetIssqn, "1");
            CbCliente.SelectedItem = null;
        }

        private void PreencherForm(NotaServicoModel n)
        {
            SetComboTag(CbAmbiente, n.Ambiente);
            TxtSerie.Text = n.Serie;
            TxtNumeroDps.Text = n.NumeroDps.ToString();
            DpCompetencia.SelectedDate = n.DataCompetencia;
            TxtIbgeEmissao.Text = n.CodigoIbgeEmissao;
            TxtTomadorNome.Text = n.TomadorNome;
            TxtTomadorDoc.Text = n.TomadorCpfCnpj;
            TxtTomadorEmail.Text = n.TomadorEmail;
            TxtTomadorFone.Text = n.TomadorFone;
            TxtTomadorLgr.Text = n.TomadorLgr;
            TxtTomadorNro.Text = n.TomadorNro;
            TxtTomadorBairro.Text = n.TomadorBairro;
            TxtTomadorMun.Text = n.TomadorMun;
            TxtTomadorUf.Text = n.TomadorUf;
            TxtTomadorCep.Text = n.TomadorCep;
            TxtTomadorIbge.Text = n.TomadorIbge;
            var cfgForm = EmpresaConfigStore.Current;
            TxtCodTribNac.Text = cfgForm.NfseCodTribNacFixo
                ? EmpresaConfigStore.NormalizarCodTribNac(cfgForm.NfseCodTribNac)
                : n.CodTribNac;
            AplicarTravaCodTribNac();
            TxtCodTribMun.Text = n.CodTribMun;
            TxtDescServico.Text = n.DescricaoServico;
            TxtCodNbs.Text = n.CodNbs;
            TxtIbgePrestacao.Text = n.CodIbgePrestacao;
            TxtValorServico.Text = n.ValorServico.ToString("N2");
            SetComboTag(CbTribIssqn, n.TribIssqn);
            SetComboTag(CbTpRetIssqn, n.TpRetIssqn);
            TxtAliqIss.Text = n.AliquotaIss.ToString("0.####");
            SetComboTag(CbOpSimpNac, n.OpSimpNac);
            SetComboTag(CbRegApTribSN, string.IsNullOrWhiteSpace(n.RegApTribSN) ? "1" : n.RegApTribSN);
            TxtRegEspTrib.Text = n.RegEspTrib;
            ChkIbsCbs.IsChecked = n.IncluirIbsCbs;
            TxtCstIbsCbs.Text = n.CstIbsCbs;
            TxtClassTrib.Text = n.ClassTrib;
            TxtCodIndOp.Text = n.CodIndOp;
        }

        /// <summary>
        /// Configurações → Fiscal define se o cTribNac é livre por nota ou fixo para a empresa.
        /// Fixo = campo somente leitura, alimentado pela configuração.
        /// </summary>
        private void AplicarTravaCodTribNac()
        {
            var cfg = EmpresaConfigStore.Current;
            bool fixo = cfg.NfseCodTribNacFixo;

            TxtCodTribNac.IsReadOnly = fixo;
            TxtCodTribNac.IsTabStop = !fixo;
            if (TryFindResource(fixo ? "WindowBackgroundBrush" : "CardBackgroundBrush")
                is System.Windows.Media.Brush fundo)
                TxtCodTribNac.Background = fundo;
            TxtCodTribNac.ToolTip = fixo
                ? "Código fixado em Configurações → Fiscal / NF-e. Para alterar, desmarque \"Fixar\" lá."
                : "Lista Nacional — ex.: 010101";

            if (LblCodTribNac != null)
                LblCodTribNac.Text = fixo ? "cTribNac (fixo) *" : "cTribNac (6 díg.) *";
        }

        private void LimparForm()
        {
            TxtTomadorNome.Text = "";
            TxtTomadorDoc.Text = "";
            TxtTomadorEmail.Text = "";
            TxtTomadorFone.Text = "";
            TxtTomadorLgr.Text = "";
            TxtTomadorNro.Text = "";
            TxtTomadorBairro.Text = "";
            TxtTomadorMun.Text = "";
            TxtTomadorUf.Text = "";
            TxtTomadorCep.Text = "";
            TxtTomadorIbge.Text = "";
            TxtCodTribMun.Text = "";
            TxtDescServico.Text = "";
            TxtCodNbs.Text = "";
            TxtValorServico.Text = "";
        }

        private void CbCliente_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CbCliente.SelectedItem is not ClienteModel c) return;
            TxtTomadorNome.Text = string.IsNullOrWhiteSpace(c.RazaoSocial) ? c.Nome : c.RazaoSocial;
            if (string.IsNullOrWhiteSpace(TxtTomadorNome.Text)) TxtTomadorNome.Text = c.Nome;
            TxtTomadorDoc.Text = Digitos(c.CpfCnpj);
            TxtTomadorEmail.Text = c.Email;
            TxtTomadorLgr.Text = c.Logradouro;
            TxtTomadorNro.Text = c.Numero;
            TxtTomadorBairro.Text = c.Bairro;
            TxtTomadorMun.Text = c.Municipio;
            TxtTomadorUf.Text = c.Uf;
            TxtTomadorCep.Text = Digitos(c.Cep);
            TxtTomadorIbge.Text = Digitos(c.CodigoIbge);
        }

        private async void TxtTomadorCep_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _buscandoCep) return;
            e.Handled = true;
            _buscandoCep = true;
            try
            {
                var result = await CepService.BuscarAsync(TxtTomadorCep.Text);
                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage ?? "CEP não encontrado.", "CEP",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TxtTomadorCep.Text = Digitos(result.Cep);
                if (!string.IsNullOrWhiteSpace(result.Logradouro)) TxtTomadorLgr.Text = result.Logradouro;
                if (!string.IsNullOrWhiteSpace(result.Bairro)) TxtTomadorBairro.Text = result.Bairro;
                if (!string.IsNullOrWhiteSpace(result.Municipio)) TxtTomadorMun.Text = result.Municipio;
                if (!string.IsNullOrWhiteSpace(result.Uf)) TxtTomadorUf.Text = result.Uf;
                if (!string.IsNullOrWhiteSpace(result.CodigoIbge)) TxtTomadorIbge.Text = result.CodigoIbge;
                TxtTomadorNro.Focus();
            }
            finally
            {
                _buscandoCep = false;
            }
        }

        private long ProximoNumero()
        {
            string serie = string.IsNullOrWhiteSpace(EmpresaConfigStore.Current.SerieNfse)
                ? "1" : EmpresaConfigStore.Current.SerieNfse.Trim();
            // Numeração da DPS é única por série (ux_nfse_serie_numero), independente do ambiente:
            // o contador das Configurações é o piso de quem migrou de outro sistema.
            long ultimo = 0;
            if (long.TryParse(EmpresaConfigStore.Current.UltimoNumeroNfse, out long cfg))
                ultimo = cfg;
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = Database.Cmd(conn,
                    "SELECT MAX(numerodps) FROM notasservico WHERE COALESCE(serie,'1') = @ser");
                cmd.Parameters.AddWithValue("@ser", serie);
                object? o = cmd.ExecuteScalar();
                if (o != null && o != DBNull.Value)
                {
                    long db = Convert.ToInt64(o);
                    if (db > ultimo) ultimo = db;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProximoNumero NFS-e: {ex.Message}");
            }
            return ultimo + 1;
        }

        private void LoadClientes()
        {
            _clientes.Clear();
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = Database.Cmd(conn, "SELECT * FROM Clientes ORDER BY Nome");
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    _clientes.Add(new ClienteModel
                    {
                        Id = Convert.ToInt64(Database.Field(r, "Id") ?? 0),
                        Nome = Database.Field(r, "Nome")?.ToString() ?? "",
                        RazaoSocial = Database.Field(r, "RazaoSocial")?.ToString() ?? "",
                        CpfCnpj = Col(r, "Cpf_Cnpj"),
                        Email = Col(r, "Email"),
                        Logradouro = Col(r, "Logradouro"),
                        Numero = Col(r, "Numero"),
                        Bairro = Col(r, "Bairro"),
                        Municipio = Col(r, "Municipio"),
                        Uf = Col(r, "Uf"),
                        Cep = Col(r, "Cep"),
                        CodigoIbge = Col(r, "CodigoIbge")
                    });
                }
            }
            catch { /* ignore */ }
            CbCliente.ItemsSource = null;
            CbCliente.ItemsSource = _clientes;
        }

        private void LoadGrid()
        {
            var list = new List<NotaServicoModel>();
            string where = "WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(_filtro))
                where += " AND (tomadornome ILIKE @q OR CAST(numerodps AS TEXT) ILIKE @q OR descricaoservico ILIKE @q)";
            string? statusTag = (CbFiltroStatus?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (!string.IsNullOrEmpty(statusTag))
                where += " AND status = @st";

            try
            {
                using var conn = Database.GetConnection();
                using (var cmdCount = Database.Cmd(conn, $"SELECT COUNT(*) FROM notasservico {where}"))
                {
                    if (!string.IsNullOrEmpty(_filtro)) cmdCount.Parameters.AddWithValue("@q", $"%{_filtro}%");
                    if (!string.IsNullOrEmpty(statusTag)) cmdCount.Parameters.AddWithValue("@st", statusTag);
                    int total = Convert.ToInt32(cmdCount.ExecuteScalar() ?? 0);
                    _totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
                    if (_page > _totalPages) _page = _totalPages;
                }

                int offset = (_page - 1) * PageSize;
                // Lista sem XMLs (pesados) — CarregarPorId traz o registro completo
                const string colsLista =
                    "id,ambiente,serie,numerodps,datacompetencia,codigoibgeemissao," +
                    "tomadornome,tomadorcpfcnpj,tomadoremail,tomadorfone," +
                    "tomadorlgr,tomadornro,tomadorbairro,tomadormun,tomadoruf,tomadorcep,tomadoribge," +
                    "codtribnac,codtribmun,descricaoservico,codnbs,codibgeprestacao,valorservico," +
                    "tribissqn,tpretissqn,aliquotaiss,opsimpnac,regaptribsn,regesptrib," +
                    "incluiribscbs,cstibscbs,classtrib,codindop," +
                    "status,chaveacesso,iddps,dataprocessamento,cstat,xmotivo,datacadastro";
                using var cmd = Database.Cmd(conn,
                    $"SELECT {colsLista} FROM notasservico {where} ORDER BY id DESC LIMIT {PageSize} OFFSET {offset}");
                if (!string.IsNullOrEmpty(_filtro)) cmd.Parameters.AddWithValue("@q", $"%{_filtro}%");
                if (!string.IsNullOrEmpty(statusTag)) cmd.Parameters.AddWithValue("@st", statusTag);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(MapRow(r));

                GridNotas.ItemsSource = list;
                if (LblPageInfo != null)
                    LblPageInfo.Text = $"Pág {_page}/{_totalPages}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "NFS-e", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static NotaServicoModel? CarregarPorId(long id)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = Database.Cmd(conn, "SELECT * FROM notasservico WHERE id=@id");
                cmd.Parameters.AddWithValue("@id", id);
                using var r = cmd.ExecuteReader();
                return r.Read() ? MapRow(r) : null;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "NFS-e", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private static NotaServicoModel MapRow(IDataRecord r) => new()
        {
            Id = Convert.ToInt64(Database.Field(r, "id") ?? 0),
            Ambiente = Col(r, "ambiente", "2"),
            Serie = Col(r, "serie", "1"),
            NumeroDps = ToLong(Database.Field(r, "numerodps")),
            DataCompetencia = LerData(r, "datacompetencia", DateTime.Today),
            CodigoIbgeEmissao = Col(r, "codigoibgeemissao"),
            TomadorNome = Col(r, "tomadornome"),
            TomadorCpfCnpj = Col(r, "tomadorcpfcnpj"),
            TomadorEmail = Col(r, "tomadoremail"),
            TomadorFone = Col(r, "tomadorfone"),
            TomadorLgr = Col(r, "tomadorlgr"),
            TomadorNro = Col(r, "tomadornro"),
            TomadorBairro = Col(r, "tomadorbairro"),
            TomadorMun = Col(r, "tomadormun"),
            TomadorUf = Col(r, "tomadoruf"),
            TomadorCep = Col(r, "tomadorcep"),
            TomadorIbge = Col(r, "tomadoribge"),
            CodTribNac = Col(r, "codtribnac", "010101"),
            CodTribMun = Col(r, "codtribmun"),
            DescricaoServico = Col(r, "descricaoservico"),
            CodNbs = Col(r, "codnbs"),
            CodIbgePrestacao = Col(r, "codibgeprestacao"),
            ValorServico = ToDec(Database.Field(r, "valorservico")),
            TribIssqn = Col(r, "tribissqn", "1"),
            TpRetIssqn = Col(r, "tpretissqn", "1"),
            AliquotaIss = ToDec(Database.Field(r, "aliquotaiss"), 5m),
            OpSimpNac = Col(r, "opsimpnac", "1"),
            RegApTribSN = Col(r, "regaptribsn", "1"),
            RegEspTrib = Col(r, "regesptrib", "0"),
            IncluirIbsCbs = ToInt(Database.Field(r, "incluiribscbs")) == 1,
            CstIbsCbs = Col(r, "cstibscbs", "000"),
            ClassTrib = Col(r, "classtrib", "000001"),
            CodIndOp = Col(r, "codindop", "000001"),
            Status = Col(r, "status", "Rascunho"),
            ChaveAcesso = Col(r, "chaveacesso"),
            IdDps = Col(r, "iddps"),
            DataProcessamento = Col(r, "dataprocessamento"),
            CStat = Col(r, "cstat"),
            XMotivo = Col(r, "xmotivo"),
            XmlEnviado = Col(r, "xmlenviado"),
            XmlAutorizado = Col(r, "xmlautorizado"),
            DataCadastro = DateTime.TryParse(Col(r, "datacadastro"), out var dcad) ? dcad : DateTime.Now
        };

        /// <summary>
        /// Lê data aceitando a coluna já tipada (DATE) e o texto ISO legado — bases anteriores
        /// à migração podem ter ficado como TEXT.
        /// </summary>
        private static DateTime LerData(IDataRecord r, string coluna, DateTime padrao)
        {
            object? v = Database.Field(r, coluna);
            if (v is DateTime dt) return dt;
            if (v is DateOnly d) return d.ToDateTime(TimeOnly.MinValue);

            string s = v?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(s)) return padrao;
            return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                       System.Globalization.DateTimeStyles.None, out var iso)
                ? iso
                : (DateTime.TryParse(s, out var qualquer) ? qualquer : padrao);
        }

        private static string Col(IDataRecord r, string name, string def = "")
        {
            try
            {
                object? v = Database.Field(r, name);
                if (v == null || v == DBNull.Value) return def;
                return v.ToString() ?? def;
            }
            catch { return def; }
        }

        private static long ToLong(object? v)
        {
            if (v == null || v == DBNull.Value) return 0;
            return Convert.ToInt64(v);
        }

        private static int ToInt(object? v)
        {
            if (v == null || v == DBNull.Value) return 0;
            return Convert.ToInt32(v);
        }

        private static decimal ToDec(object? v, decimal def = 0)
        {
            if (v == null || v == DBNull.Value) return def;
            return Convert.ToDecimal(v);
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

        private static string Digitos(string? s) =>
            string.IsNullOrWhiteSpace(s) ? "" : new string(Array.FindAll(s.ToCharArray(), char.IsDigit));
    }
}
