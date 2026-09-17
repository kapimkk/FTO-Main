using FTO_App.Models;
using FTO_App.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Npgsql;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FTO_App.Views
{
    public partial class NotaFiscalView : UserControl
    {
        private const int PageSize = 50;
        private const int NatOpMaxLength = 60;
        private long? _editingId;
        private readonly List<ClienteModel> _clientes = new();
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
        private string _filtro = "";
        private int _page = 1;
        private int _totalPages = 1;
        private bool _buscandoCep;

        /// <summary>Itens da nota em edição — a grade do formulário é ligada direto nesta coleção.</summary>
        private readonly ObservableCollection<NotaFiscalItemModel> _itens = new();

        public NotaFiscalView()
        {
            InitializeComponent();
            DpEmissao.SelectedDate = DateTime.Today;
            GridItens.ItemsSource = _itens;
            Loaded += (_, _) =>
            {
                LoadClientes();
                AtualizarHintHomolog();
                AtualizarResumoItens();
                LoadGrid();
            };
        }

        private static bool RegimeNormal => EmpresaConfigStore.Current.RegimeTributario == "3";

        private void AtualizarHintHomolog()
        {
            if (LblHomologHint == null || CbAmbiente == null) return;
            bool homolog = (CbAmbiente.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "2";

            LblHomologHint.Visibility = homolog ? Visibility.Visible : Visibility.Collapsed;
            LblBadgeAmbiente.Text = homolog ? "HOMOLOGAÇÃO" : "PRODUÇÃO";
            BadgeAmbiente.Background = new SolidColorBrush(homolog
                ? Color.FromRgb(0xFE, 0xF3, 0xC7)
                : Color.FromRgb(0xDC, 0xFC, 0xE7));
            LblBadgeAmbiente.Foreground = new SolidColorBrush(homolog
                ? Color.FromRgb(0xB4, 0x53, 0x09)
                : Color.FromRgb(0x15, 0x80, 0x3D));
        }

        private void CbAmbiente_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            AtualizarHintHomolog();
        }

        private void CbIndIEDest_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || TxtDestIe == null) return;
            string? ind = (CbIndIEDest.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (ind == "2" && string.IsNullOrWhiteSpace(TxtDestIe.Text))
                TxtDestIe.Text = "ISENTO";
            else if (ind == "9")
                TxtDestIe.Text = "";
        }

        private void TxtDestIe_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || CbIndIEDest == null) return;
            string ie = (TxtDestIe.Text ?? "").Trim();
            if (string.Equals(ie, "ISENTO", StringComparison.OrdinalIgnoreCase))
            {
                SetComboTag(CbIndIEDest, "2");
                return;
            }
            // IE numérica → contribuinte (evita salvar ind=9 com IE preenchida)
            if (ie.Any(char.IsDigit))
                SetComboTag(CbIndIEDest, "1");
        }

        private void TxtDestUf_TextChanged(object sender, TextChangedEventArgs e) => SugerirIdDest();

        /// <summary>idDest segue o CFOP do 1º item (5xxx/6xxx/7xxx) e a UF do destinatário.</summary>
        private void SugerirIdDest()
        {
            if (CbIdDest == null || !IsLoaded) return;
            string id = NfeXmlService.InferirIdDest(
                _itens.FirstOrDefault()?.Cfop,
                EmpresaConfigStore.Current.Uf,
                TxtDestUf?.Text);
            SetComboTag(CbIdDest, id);
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

        /// <summary>Abre o cadastro em branco para nova NF-e (modelo 55).</summary>
        private void AbrirNovo()
        {
            BtnLimpar_Click(this, new RoutedEventArgs());
            AtualizarTituloForm();
            if (BtnExcluirForm != null) BtnExcluirForm.Visibility = Visibility.Collapsed;
            FormOverlay.Visibility = Visibility.Visible;
        }

        private void BtnNovoNfe_Click(object sender, RoutedEventArgs e) => AbrirNovo();

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if (GridNotas.SelectedItem is not NotaFiscalModel n)
            {
                MessageBox.Show("Selecione uma nota na lista.", "NF-e", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            AbrirEdicao(n);
        }

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridNotas.SelectedItem is NotaFiscalModel n)
                AbrirEdicao(n);
        }

        private void AbrirEdicao(NotaFiscalModel n)
        {
            CarregarNotaNoForm(n);
            AtualizarTituloForm();
            if (BtnExcluirForm != null) BtnExcluirForm.Visibility = Visibility.Visible;
            FormOverlay.Visibility = Visibility.Visible;
        }

        /// <summary>Abre a janela de emissão/consulta/cancelamento/CC-e/DANFE para a
        /// nota selecionada — o cadastro (esta tela) fica só com o lançamento (Salvar/Excluir).</summary>
        private void BtnAcoesFiscais_Click(object sender, RoutedEventArgs e)
        {
            if (GridNotas.SelectedItem is not NotaFiscalModel sel)
            {
                MessageBox.Show("Selecione uma nota na lista.", "Nota Fiscal", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var nota = CarregarNotaPorId(sel.Id);
            if (nota == null)
            {
                MessageBox.Show("Não foi possível carregar os dados da nota selecionada.", "Nota Fiscal",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var win = new NotaFiscalAcoesWindow(nota) { Owner = Window.GetWindow(this) };
            win.ShowDialog();
            if (win.HouveAlteracao) LoadGrid();
        }

        private void BtnExcluirLista_Click(object sender, RoutedEventArgs e)
        {
            if (GridNotas.SelectedItem is not NotaFiscalModel n)
            {
                MessageBox.Show("Selecione uma nota na lista.", "NF-e", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ExcluirNota(n.Id, n.NumeroExibicao, n.DestNome);
        }

        private void BtnExcluirForm_Click(object sender, RoutedEventArgs e)
        {
            if (!_editingId.HasValue) return;
            string num = $"{TxtSerie?.Text}/{TxtNumero?.Text}";
            ExcluirNota(_editingId.Value, num, TxtDestNome?.Text);
        }

        private void ExcluirNota(long id, string numero, string? dest)
        {
            string label = string.IsNullOrWhiteSpace(dest) ? numero : $"{numero} — {dest}";
            if (MessageBox.Show($"Excluir a nota fiscal \"{label}\"?\n\nEsta ação não pode ser desfeita.",
                    "Confirmar exclusão", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                Database.ExecuteNonQuery("DELETE FROM NotasFiscais WHERE Id=@id",
                    new Dictionary<string, object> { ["@id"] = id });
                if (_editingId == id)
                {
                    FormOverlay.Visibility = Visibility.Collapsed;
                    BtnLimpar_Click(this, new RoutedEventArgs());
                }
                LoadGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao excluir: {ex.Message}", "NF-e", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnFecharForm_Click(object sender, RoutedEventArgs e)
        {
            // Fechar com itens lançados perde o trabalho — confirma antes.
            if (_itens.Count > 0 && !_editingId.HasValue &&
                MessageBox.Show($"Descartar esta nota com {_itens.Count} item(ns) lançado(s)?",
                    "NF-e", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            FormOverlay.Visibility = Visibility.Collapsed;
            BtnLimpar_Click(sender, e);
        }

        private void BtnFiltrar_Click(object sender, RoutedEventArgs e)
        {
            _filtro = TxtBusca.Text.Trim();
            _page = 1;
            LoadGrid();
        }

        private void TxtBusca_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filtro = TxtBusca.Text.Trim();
            _page = 1;
            LoadGrid();
        }

        private void Filtro_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
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
                        Id = Convert.ToInt64(Database.FieldOrDbNull(r, "Id")),
                        Nome = Database.FieldOrDbNull(r, "Nome")?.ToString() ?? "",
                        CpfCnpj = Col(r, "Cpf_Cnpj"),
                        Ie = Col(r, "Ie"),
                        Email = Col(r, "Email"),
                        Logradouro = Col(r, "Logradouro"),
                        Numero = Col(r, "Numero"),
                        Complemento = Col(r, "Complemento"),
                        Bairro = Col(r, "Bairro"),
                        Municipio = Col(r, "Municipio"),
                        Uf = Col(r, "Uf"),
                        Cep = Col(r, "Cep"),
                        CodigoIbge = Col(r, "CodigoIbge")
                    });
                }
            }
            catch { }

            CbCliente.ItemsSource = null;
            CbCliente.ItemsSource = _clientes;
        }

        /// <summary>
        /// Ambiente da NF-e não é mais uma escolha por nota: Configurações → Fiscal / NF-e é a
        /// única fonte. Uma nota já emitida (ou cancelada) preserva o ambiente real da emissão —
        /// mudar a configuração depois não pode reescrever a história de uma nota que já foi para a SEFAZ.
        /// </summary>
        private static string AmbienteFixoConfig(EmpresaConfig? cfg = null) =>
            FiscalApiClient.NormalizarTpAmb((cfg ?? EmpresaConfigStore.Current).AmbienteNfe);

        private static bool NotaJaDefinida(NotaFiscalModel n) =>
            n.TemChaveAcesso ||
            string.Equals(n.Status, "Emitida", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.Status, "Cancelada", StringComparison.OrdinalIgnoreCase);

        private void SugerirProximoNumero()
        {
            try
            {
                var cfgEmpresa = EmpresaConfigStore.Current;
                string serie = string.IsNullOrWhiteSpace(cfgEmpresa.SerieNfe) ? "1" : cfgEmpresa.SerieNfe.Trim();
                string ambiente = AmbienteFixoConfig(cfgEmpresa);

                // "Último nº NF-e" das Configurações é o contador de PRODUÇÃO — em homologação a
                // sugestão sai só do que existe no banco, para teste não abrir buraco na numeração oficial.
                long ultimo = 0;
                if (ambiente == "1" && long.TryParse(cfgEmpresa.UltimoNumeroNfe, out long cfg))
                    ultimo = cfg;

                // A numeração da NF-e é por série e por ambiente — MAX() global fazia um teste em
                // homologação consumir número da produção (e vice-versa).
                using var conn = Database.GetConnection();
                using var cmd = Database.Cmd(conn,
                    "SELECT MAX(Numero) FROM NotasFiscais WHERE COALESCE(Serie,'1') = @ser AND COALESCE(Ambiente,'2') = @amb");
                cmd.Parameters.AddWithValue("@ser", serie);
                cmd.Parameters.AddWithValue("@amb", ambiente);
                var scalar = cmd.ExecuteScalar();
                if (scalar != null && scalar != DBNull.Value)
                    ultimo = Math.Max(ultimo, Convert.ToInt64(scalar));

                TxtNumero.Text = (ultimo + 1).ToString();
                TxtSerie.Text = serie;
                CbAmbiente.SelectedIndex = ambiente == "1" ? 0 : 1;
            }
            catch
            {
                TxtNumero.Text = "1";
            }
        }

        private void CbCliente_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbCliente.SelectedItem is not ClienteModel c) return;
            TxtDestNome.Text = c.Nome;
            TxtDestDoc.Text = c.CpfCnpj;
            TxtDestIe.Text = c.Ie;
            TxtDestEmail.Text = c.Email;
            TxtDestLgr.Text = c.Logradouro;
            TxtDestNro.Text = c.Numero;
            TxtDestBairro.Text = c.Bairro;
            TxtDestMun.Text = c.Municipio;
            TxtDestUf.Text = c.Uf;
            TxtDestCep.Text = c.Cep;
            TxtDestIbge.Text = c.CodigoIbge;
            if (!string.IsNullOrWhiteSpace(c.Ie))
            {
                if (string.Equals(c.Ie.Trim(), "ISENTO", StringComparison.OrdinalIgnoreCase))
                    SetComboTag(CbIndIEDest, "2");
                else
                    SetComboTag(CbIndIEDest, "1");
            }
            else
            {
                SetComboTag(CbIndIEDest, "9");
            }
            SugerirIdDest();
        }

        private async void TxtDestCep_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _buscandoCep) return;
            e.Handled = true;
            _buscandoCep = true;
            try
            {
                var result = await CepService.BuscarAsync(TxtDestCep.Text);
                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage ?? "CEP não encontrado.", "CEP",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TxtDestCep.Text = result.Cep;
                if (!string.IsNullOrWhiteSpace(result.Logradouro)) TxtDestLgr.Text = result.Logradouro;
                if (!string.IsNullOrWhiteSpace(result.Bairro)) TxtDestBairro.Text = result.Bairro;
                if (!string.IsNullOrWhiteSpace(result.Municipio)) TxtDestMun.Text = result.Municipio;
                if (!string.IsNullOrWhiteSpace(result.Uf)) TxtDestUf.Text = result.Uf;
                if (!string.IsNullOrWhiteSpace(result.CodigoIbge)) TxtDestIbge.Text = result.CodigoIbge;
                TxtDestNro.Focus();
            }
            finally
            {
                _buscandoCep = false;
            }
        }

        // -----------------------------------------------------------------
        // Itens da nota
        // -----------------------------------------------------------------

        private Window? Dono => Window.GetWindow(this);

        private void BtnAdicionarItem_Click(object sender, RoutedEventArgs e)
        {
            var win = NovaJanelaAdicionar();
            FinalizarJanelaAdicionar(win, win.ShowDialog());
        }

        private void BtnItemDoEstoque_Click(object sender, RoutedEventArgs e)
        {
            var win = NovaJanelaAdicionar();
            if (!win.EscolherDoEstoque()) return;
            FinalizarJanelaAdicionar(win, win.ShowDialog());
        }

        /// <summary>
        /// Cada "Salvar e adicionar outro" já entra na grade na hora (callback); o item-modelo é o
        /// último da nota, para herdar CFOP, unidade e tributação.
        /// </summary>
        private NotaFiscalItemWindow NovaJanelaAdicionar() =>
            new(item: null,
                numeroItem: _itens.Count + 1,
                aoAdicionarEmSequencia: AdicionarItem,
                modelo: _itens.LastOrDefault())
            {
                Owner = Dono
            };

        private void FinalizarJanelaAdicionar(NotaFiscalItemWindow win, bool? resultado)
        {
            if (resultado == true && win.Resultado != null)
                AdicionarItem(win.Resultado);
        }

        private void AdicionarItem(NotaFiscalItemModel item)
        {
            bool primeiro = _itens.Count == 0;
            _itens.Add(item);
            AtualizarResumoItens();
            GridItens.SelectedItem = item;
            GridItens.ScrollIntoView(item);
            if (primeiro) SugerirIdDest();
        }

        private void EditarItem(NotaFiscalItemModel item)
        {
            int idx = _itens.IndexOf(item);
            if (idx < 0) return;

            var win = new NotaFiscalItemWindow(item, idx + 1) { Owner = Dono };
            if (win.ShowDialog() != true || win.Resultado == null) return;

            _itens[idx] = win.Resultado;
            AtualizarResumoItens();
            GridItens.SelectedItem = win.Resultado;
            if (idx == 0) SugerirIdDest();
        }

        private void DuplicarItem(NotaFiscalItemModel item)
        {
            int idx = _itens.IndexOf(item);
            if (idx < 0) return;

            var copia = item.Clonar();
            _itens.Insert(idx + 1, copia);
            AtualizarResumoItens();
            GridItens.SelectedItem = copia;
            GridItens.ScrollIntoView(copia);
        }

        private void RemoverItem(NotaFiscalItemModel item)
        {
            if (MessageBox.Show($"Remover o item \"{item.Descricao}\" da nota?", "Remover item",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            int idx = _itens.IndexOf(item);
            _itens.Remove(item);
            AtualizarResumoItens();
            if (idx == 0) SugerirIdDest();
        }

        private static NotaFiscalItemModel? ItemDaLinha(object sender) =>
            (sender as FrameworkElement)?.DataContext as NotaFiscalItemModel;

        private void BtnLinhaEditar_Click(object sender, RoutedEventArgs e)
        {
            if (ItemDaLinha(sender) is { } item) EditarItem(item);
        }

        private void BtnLinhaDuplicar_Click(object sender, RoutedEventArgs e)
        {
            if (ItemDaLinha(sender) is { } item) DuplicarItem(item);
        }

        private void BtnLinhaRemover_Click(object sender, RoutedEventArgs e)
        {
            if (ItemDaLinha(sender) is { } item) RemoverItem(item);
        }

        private void GridItens_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Duplo clique no cabeçalho ou na área vazia não é edição.
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(GridItens, d) is DataGridRow row &&
                row.Item is NotaFiscalItemModel item)
                EditarItem(item);
        }

        private void GridItens_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && GridItens.SelectedItem is NotaFiscalItemModel item)
            {
                e.Handled = true;
                RemoverItem(item);
            }
            else if (e.Key == Key.Enter && GridItens.SelectedItem is NotaFiscalItemModel sel)
            {
                e.Handled = true;
                EditarItem(sel);
            }
        }

        /// <summary>Renumera, atualiza estado vazio, contagem e rodapé de totais.</summary>
        private void AtualizarResumoItens()
        {
            for (int i = 0; i < _itens.Count; i++)
                _itens[i].Posicao = i + 1;
            GridItens.Items.Refresh();

            int qtd = _itens.Count;
            PainelItensVazio.Visibility = qtd == 0 ? Visibility.Visible : Visibility.Collapsed;
            LblItensContagem.Text = qtd switch
            {
                0 => "Nenhum item — a nota precisa de pelo menos um",
                1 => "1 item",
                _ => $"{qtd} itens"
            };

            decimal produtos = _itens.Sum(i => i.ValorTotal);
            decimal icms;
            if (RegimeNormal)
            {
                LblTotIcmsRotulo.Text = "ICMS";
                icms = _itens.Where(i => !FiscalPayloadBuilder.IcmsSemBase(i.IcmsCst?.Trim() ?? "")).Sum(i => i.IcmsValor);
            }
            else
            {
                LblTotIcmsRotulo.Text = "CRÉDITO ICMS (SN)";
                icms = _itens.Where(i => (i.Csosn ?? "").Trim() == "101").Sum(i => i.IcmsValor);
            }

            decimal pisCofins = _itens.Sum(i =>
                (FiscalPayloadBuilder.PisCofinsNaoTributado((i.PisCst ?? "").Trim()) ? 0 : i.PisValor) +
                (FiscalPayloadBuilder.PisCofinsNaoTributado((i.CofinsCst ?? "").Trim()) ? 0 : i.CofinsValor));

            LblTotItens.Text = qtd.ToString(PtBr);
            LblTotProdutos.Text = produtos.ToString("C2", PtBr);
            LblTotIcms.Text = icms.ToString("C2", PtBr);
            LblTotPisCofins.Text = pisCofins.ToString("C2", PtBr);
            LblTotIbsCbs.Text = _itens.Sum(i => i.IbsValor + i.CbsValor).ToString("C2", PtBr);
            LblTotNota.Text = produtos.ToString("C2", PtBr);
        }

        // -----------------------------------------------------------------
        // Salvar
        // -----------------------------------------------------------------

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtDestNome.Text))
            {
                MessageBox.Show("Informe o destinatário da nota.", "NF-e", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtDestNome.Focus();
                return;
            }

            if (_itens.Count == 0)
            {
                MessageBox.Show("Adicione pelo menos um item à nota.", "NF-e", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Itens vindos de nota antiga podem estar incompletos — deixa salvar como rascunho,
            // mas avisa: a emissão vai recusar do mesmo jeito.
            var problemas = NotaFiscalValidacao.ValidarItens(_itens.ToList());
            if (problemas.Count > 0 &&
                MessageBox.Show("Há itens incompletos:\n\n• " + string.Join("\n• ", problemas) +
                                "\n\nSalvar mesmo assim como rascunho? A emissão só será liberada depois de corrigir.",
                    "NF-e", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                var nota = MontarNota();
                SalvarNoBanco(nota);
                MessageBox.Show($"Nota salva com {nota.Itens.Count} item(ns) — total {nota.ValorTotalNota.ToString("C2", PtBr)}.",
                    "NF-e", MessageBoxButton.OK, MessageBoxImage.Information);
                FormOverlay.Visibility = Visibility.Collapsed;
                BtnLimpar_Click(sender, e);
                LoadGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar: {ex.Message}", "NF-e", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Insere/atualiza a nota no banco (sem fechar o formulário) — reaproveitado antes de emitir na API.</summary>
        private void SalvarNoBanco(NotaFiscalModel nota)
        {
            var p = Parametros(nota);

            if (_editingId.HasValue)
            {
                p["@id"] = _editingId.Value;
                Database.ExecuteNonQuery(@"UPDATE NotasFiscais SET NaturezaOperacao=@nat, Modelo=@mod, Serie=@ser, Numero=@num,
                    DataEmissao=@dem, TipoOperacao=@top, Finalidade=@fin, ConsumidorFinal=@cf, PresencaComprador=@pres, Ambiente=@amb,
                    ClienteId=@cid, DestNome=@dn, DestCpfCnpj=@dd, DestIe=@die, DestEmail=@demail, DestLogradouro=@dl,
                    DestNumero=@dnr, DestComplemento=@dcm, DestBairro=@dba, DestMunicipio=@dmu, DestUf=@duf, DestCep=@dce,
                    DestCodigoIbge=@dib, ProdutoCodigo=@pcod, ProdutoDescricao=@pd, ProdutoNcm=@pn, ProdutoCfop=@pf,
                    ProdutoUnidade=@pu, ProdutoQuantidade=@pq, ProdutoValorUnitario=@pvu, ProdutoValorTotal=@pvt,
                    IcmsOrigem=@io, IcmsCst=@ic, IcmsAliquota=@ia, IcmsValor=@iv, PisCst=@psc, PisAliquota=@psa, PisValor=@psv,
                    CofinsCst=@csc, CofinsAliquota=@csa, CofinsValor=@csv, ValorProdutos=@vp, ValorFrete=@vf, ValorDesconto=@vd,
                    ValorTotalNota=@vn, FormaPagamento=@fp, InformacoesComplementares=@inf, Status=@st, CaminhoXml=@xml
                    WHERE Id=@id", p);
            }
            else
            {
                _editingId = Database.ExecuteInsertReturnId(@"INSERT INTO NotasFiscais
                    (NaturezaOperacao,Modelo,Serie,Numero,DataEmissao,TipoOperacao,Finalidade,ConsumidorFinal,PresencaComprador,Ambiente,
                     ClienteId,DestNome,DestCpfCnpj,DestIe,DestEmail,DestLogradouro,DestNumero,DestComplemento,DestBairro,DestMunicipio,DestUf,DestCep,DestCodigoIbge,
                     ProdutoCodigo,ProdutoDescricao,ProdutoNcm,ProdutoCfop,ProdutoUnidade,ProdutoQuantidade,ProdutoValorUnitario,ProdutoValorTotal,
                     IcmsOrigem,IcmsCst,IcmsAliquota,IcmsValor,PisCst,PisAliquota,PisValor,CofinsCst,CofinsAliquota,CofinsValor,
                     ValorProdutos,ValorFrete,ValorDesconto,ValorTotalNota,FormaPagamento,InformacoesComplementares,Status,CaminhoXml)
                    VALUES (@nat,@mod,@ser,@num,@dem,@top,@fin,@cf,@pres,@amb,@cid,@dn,@dd,@die,@demail,@dl,@dnr,@dcm,@dba,@dmu,@duf,@dce,@dib,
                     @pcod,@pd,@pn,@pf,@pu,@pq,@pvu,@pvt,@io,@ic,@ia,@iv,@psc,@psa,@psv,@csc,@csa,@csv,@vp,@vf,@vd,@vn,@fp,@inf,@st,@xml)", p);
            }

            AtualizarUltimoNumero(nota.Numero);
            if (_editingId.HasValue)
            {
                nota.Id = _editingId.Value;
                SalvarCamposExtras(_editingId.Value, nota);
            }
        }

        // -----------------------------------------------------------------
        // Inutilização de numeração — única ação da API Fiscal que continua
        // aqui (não é sobre uma nota específica, e sim sobre uma faixa de
        // números nunca emitidos; todas as demais ações vivem em
        // NotaFiscalAcoesWindow, aberta pelo botão "⚡ Ações fiscais").
        // -----------------------------------------------------------------

        /// <summary>Título e subtítulo do modal refletem se é lançamento novo ou edição.</summary>
        private void AtualizarTituloForm()
        {
            if (LblFormTitulo == null) return;
            LblFormTitulo.Text = _editingId.HasValue ? "Editar NF-e" : "Nova NF-e";
            LblFormSubtitulo.Text = $"Modelo 55 · Série {TxtSerie.Text} · Nº {TxtNumero.Text}";
        }

        private string NaturezaOperacaoAtual()
        {
            string nat = (CbNatOp?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nat))
                nat = "Venda de mercadoria";
            if (nat.Length > NatOpMaxLength)
                nat = nat[..NatOpMaxLength];
            return nat;
        }

        private async void BtnInutilizar_Click(object sender, RoutedEventArgs e)
        {
            var cfg = EmpresaConfigStore.Current;
            if (string.IsNullOrWhiteSpace(cfg.Cnpj) || string.IsNullOrWhiteSpace(cfg.Uf))
            {
                MessageBox.Show("Complete CNPJ e UF da empresa em Configurações antes de inutilizar numeração.",
                    "Inutilização", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var win = new InutilizacaoWindow(cfg.SerieNfe) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() != true) return;

            string tpAmb = FiscalApiClient.NormalizarTpAmb(cfg.AmbienteNfe);
            var resultado = await FiscalApiClient.InutilizarAsync(
                cfg.FiscalApiUrlNfe, cfg.FiscalApiKey, cfg.Cnpj, FiscalPayloadBuilder.UfToCodigo(cfg.Uf),
                win.Ano, win.Serie, win.NumeroInicial, win.NumeroFinal, win.Justificativa, tpAmb);

            if (!resultado.Sucesso)
            {
                MessageBox.Show($"Falha ao inutilizar a faixa:\n\n{resultado.ResumoErro()}", "Inutilização",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dados = resultado.Dados!;
            MessageBox.Show(dados.Aprovado
                    ? $"✅ Faixa {win.NumeroInicial}-{win.NumeroFinal} (série {win.Serie}) inutilizada com sucesso!\n\nProtocolo: {dados.NProt}\n{dados.CStat} - {dados.XMotivo}"
                    : $"⚠️ Inutilização não foi aceita pela SEFAZ.\n\n{dados.CStat} - {(dados.MensagemTraduzida ?? dados.XMotivo)}",
                "Inutilização", MessageBoxButton.OK, dados.Aprovado ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void BtnLimpar_Click(object sender, RoutedEventArgs e)
        {
            _editingId = null;
            CbCliente.SelectedItem = null;
            CbCliente.Text = "";
            TxtDestNome.Text = TxtDestDoc.Text = TxtDestIe.Text = TxtDestEmail.Text = "";
            TxtDestLgr.Text = TxtDestNro.Text = TxtDestBairro.Text = TxtDestMun.Text = "";
            TxtDestUf.Text = TxtDestCep.Text = TxtDestIbge.Text = "";
            SetComboTag(CbIndIEDest, "9");
            SetComboTag(CbIdDest, "1");
            SetComboTag(CbIndFinal, "1");
            SetComboTag(CbIndPres, "1");
            SetComboTag(CbTipoOp, "1");
            SetComboTag(CbFinalidade, "1");
            SetComboTag(CbFormaPag, "01");
            DpEmissao.SelectedDate = DateTime.Today;
            TxtInfCpl.Text = "";
            CbNatOp.Text = "Venda de mercadoria";
            _itens.Clear();
            if (BtnExcluirForm != null) BtnExcluirForm.Visibility = Visibility.Collapsed;
            SugerirProximoNumero();
            AtualizarHintHomolog();
            AtualizarTituloForm();
            AtualizarResumoItens();
        }

        private void SalvarCamposExtras(long id, NotaFiscalModel n)
        {
            Database.ExecuteNonQuery(@"UPDATE NotasFiscais SET CstIbsCbs=@cst, ClassTrib=@ct,
                CbsAliquota=@ca, CbsValor=@cv, IbsAliquota=@ia, IbsValor=@iv,
                IbsAliquotaUf=@iau, IbsValorUf=@ivu, IbsAliquotaMun=@iam, IbsValorMun=@ivm,
                IdDest=@idd, IndIEDest=@iie, Csosn=@csosn, ProdutoCest=@cest, ProdutoGtin=@gtin,
                IcmsOrigem=@io, IcmsCst=@icst, PisCst=@psc, CofinsCst=@csc, ItensJson=@itens
                WHERE Id=@id",
                new Dictionary<string, object>
                {
                    ["@cst"] = n.CstIbsCbs, ["@ct"] = n.ClassTrib,
                    ["@ca"] = n.CbsAliquota, ["@cv"] = n.CbsValor,
                    ["@ia"] = n.IbsAliquota, ["@iv"] = n.IbsValor,
                    ["@iau"] = n.IbsAliquotaUf, ["@ivu"] = n.IbsValorUf,
                    ["@iam"] = n.IbsAliquotaMun, ["@ivm"] = n.IbsValorMun,
                    ["@idd"] = n.IdDest, ["@iie"] = n.IndIEDest,
                    ["@csosn"] = n.Csosn, ["@cest"] = n.ProdutoCest, ["@gtin"] = n.ProdutoGtin,
                    ["@io"] = n.IcmsOrigem, ["@icst"] = n.IcmsCst,
                    ["@psc"] = n.PisCst, ["@csc"] = n.CofinsCst,
                    ["@itens"] = n.SerializarItens(),
                    ["@id"] = id
                });
        }

        private void CarregarNotaNoForm(NotaFiscalModel n)
        {
            _editingId = n.Id;
            CbNatOp.Text = string.IsNullOrWhiteSpace(n.NaturezaOperacao) ? "Venda de mercadoria" : n.NaturezaOperacao;
            TxtSerie.Text = n.Serie;
            TxtNumero.Text = n.Numero.ToString();
            DpEmissao.SelectedDate = n.DataEmissao;
            // Rascunho segue a configuração atual da empresa; nota já emitida/cancelada mantém
            // o ambiente real em que foi transmitida (não pode ser reescrito depois).
            SetComboTag(CbAmbiente, NotaJaDefinida(n) ? n.Ambiente : AmbienteFixoConfig());
            SetComboTag(CbTipoOp, n.TipoOperacao);
            SetComboTag(CbFinalidade, n.Finalidade);
            SetComboTag(CbIdDest, string.IsNullOrWhiteSpace(n.IdDest) ? "1" : n.IdDest);
            SetComboTag(CbIndFinal, n.ConsumidorFinal);
            SetComboTag(CbIndPres, n.PresencaComprador);
            SetComboTag(CbIndIEDest, string.IsNullOrWhiteSpace(n.IndIEDest) ? "9" : n.IndIEDest);
            SetComboTag(CbFormaPag, string.IsNullOrWhiteSpace(n.FormaPagamento) ? "01" : n.FormaPagamento);
            CbCliente.SelectedItem = null;
            CbCliente.Text = "";
            TxtDestNome.Text = n.DestNome;
            TxtDestDoc.Text = n.DestCpfCnpj;
            TxtDestIe.Text = n.DestIe;
            TxtDestEmail.Text = n.DestEmail;
            TxtDestLgr.Text = n.DestLogradouro;
            TxtDestNro.Text = n.DestNumero;
            TxtDestBairro.Text = n.DestBairro;
            TxtDestMun.Text = n.DestMunicipio;
            TxtDestUf.Text = n.DestUf;
            TxtDestCep.Text = n.DestCep;
            TxtDestIbge.Text = n.DestCodigoIbge;
            TxtInfCpl.Text = n.InformacoesComplementares;

            _itens.Clear();
            n.GarantirItens();
            foreach (var item in n.Itens)
                _itens.Add(item.Clonar());

            AtualizarHintHomolog();
            AtualizarResumoItens();
        }

        private NotaFiscalModel MontarNota()
        {
            long.TryParse(TxtNumero.Text, out long numero);
            var cliente = CbCliente.SelectedItem as ClienteModel;
            var cfg = EmpresaConfigStore.Current;

            string idDest = GetComboTag(CbIdDest, NfeXmlService.InferirIdDest(
                _itens.FirstOrDefault()?.Cfop, cfg.Uf, TxtDestUf.Text));

            var (indIe, ieDest) = NfeXmlService.ConciliarIndIeDest(
                GetComboTag(CbIndIEDest, "9"), TxtDestIe.Text);

            var nota = new NotaFiscalModel
            {
                NaturezaOperacao = NaturezaOperacaoAtual(),
                Modelo = "55",
                Serie = TxtSerie.Text.Trim(),
                Numero = numero,
                DataEmissao = DpEmissao.SelectedDate ?? DateTime.Now,
                TipoOperacao = GetComboTag(CbTipoOp, "1"),
                Finalidade = GetComboTag(CbFinalidade, "1"),
                ConsumidorFinal = GetComboTag(CbIndFinal, "1"),
                PresencaComprador = GetComboTag(CbIndPres, "1"),
                Ambiente = GetComboTag(CbAmbiente, "2"),
                IdDest = idDest,
                ClienteId = cliente?.Id,
                DestNome = TxtDestNome.Text.Trim(),
                DestCpfCnpj = TxtDestDoc.Text.Trim(),
                DestIe = ieDest,
                IndIEDest = indIe,
                DestEmail = TxtDestEmail.Text.Trim(),
                DestLogradouro = TxtDestLgr.Text.Trim(),
                DestNumero = TxtDestNro.Text.Trim(),
                DestBairro = TxtDestBairro.Text.Trim(),
                DestMunicipio = TxtDestMun.Text.Trim(),
                DestUf = TxtDestUf.Text.Trim().ToUpperInvariant(),
                DestCep = TxtDestCep.Text.Trim(),
                DestCodigoIbge = TxtDestIbge.Text.Trim(),
                FormaPagamento = GetComboTag(CbFormaPag, "01"),
                InformacoesComplementares = TxtInfCpl.Text.Trim(),
                Status = "Rascunho",
                Itens = _itens.Select(i => i.Clonar()).ToList()
            };

            // Soma os itens e espelha o 1º nas colunas antigas da tabela.
            nota.RecalcularTotais();
            return nota;
        }

        private Dictionary<string, object> Parametros(NotaFiscalModel n) => new()
        {
            ["@nat"] = n.NaturezaOperacao, ["@mod"] = n.Modelo, ["@ser"] = n.Serie, ["@num"] = n.Numero,
            // Coluna é TIMESTAMP — enviar DateTime, não texto formatado
            ["@dem"] = DateTime.SpecifyKind(n.DataEmissao, DateTimeKind.Unspecified),
            ["@top"] = n.TipoOperacao, ["@fin"] = n.Finalidade, ["@cf"] = n.ConsumidorFinal,
            ["@pres"] = n.PresencaComprador, ["@amb"] = n.Ambiente,
            ["@cid"] = n.ClienteId.HasValue ? n.ClienteId.Value : DBNull.Value,
            ["@dn"] = n.DestNome, ["@dd"] = n.DestCpfCnpj, ["@die"] = n.DestIe, ["@demail"] = n.DestEmail,
            ["@dl"] = n.DestLogradouro, ["@dnr"] = n.DestNumero, ["@dcm"] = n.DestComplemento,
            ["@dba"] = n.DestBairro, ["@dmu"] = n.DestMunicipio, ["@duf"] = n.DestUf,
            ["@dce"] = n.DestCep, ["@dib"] = n.DestCodigoIbge,
            ["@pcod"] = n.ProdutoCodigo, ["@pd"] = n.ProdutoDescricao, ["@pn"] = n.ProdutoNcm,
            ["@pf"] = n.ProdutoCfop, ["@pu"] = n.ProdutoUnidade, ["@pq"] = n.ProdutoQuantidade,
            ["@pvu"] = n.ProdutoValorUnitario, ["@pvt"] = n.ProdutoValorTotal,
            ["@io"] = n.IcmsOrigem, ["@ic"] = n.IcmsCst, ["@ia"] = n.IcmsAliquota, ["@iv"] = n.IcmsValor,
            ["@psc"] = n.PisCst, ["@psa"] = n.PisAliquota, ["@psv"] = n.PisValor,
            ["@csc"] = n.CofinsCst, ["@csa"] = n.CofinsAliquota, ["@csv"] = n.CofinsValor,
            ["@vp"] = n.ValorProdutos, ["@vf"] = n.ValorFrete, ["@vd"] = n.ValorDesconto, ["@vn"] = n.ValorTotalNota,
            ["@fp"] = n.FormaPagamento, ["@inf"] = n.InformacoesComplementares,
            ["@st"] = n.Status, ["@xml"] = (object?)n.CaminhoXml ?? DBNull.Value
        };

        private void LoadGrid()
        {
            var list = new List<NotaFiscalModel>();
            string where = "WHERE 1=1";
            if (!string.IsNullOrEmpty(_filtro))
                // ILIKE: no PostgreSQL o LIKE é sensível a maiúsculas (no SQLite legado não era)
                where += " AND (DestNome ILIKE @q OR CAST(Numero AS TEXT) ILIKE @q OR Status ILIKE @q)";

            string? statusTag = (CbFiltroStatus?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (!string.IsNullOrEmpty(statusTag))
                where += " AND Status = @st";

            try
            {
                using var conn = Database.GetConnection();

                using (var cmdCount = Database.Cmd(conn, $"SELECT COUNT(*) FROM NotasFiscais {where}"))
                {
                    if (!string.IsNullOrEmpty(_filtro)) cmdCount.Parameters.AddWithValue("@q", $"%{_filtro}%");
                    if (!string.IsNullOrEmpty(statusTag)) cmdCount.Parameters.AddWithValue("@st", statusTag);
                    int total = Convert.ToInt32(cmdCount.ExecuteScalar() ?? 0);
                    _totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
                    if (_page > _totalPages) _page = _totalPages;
                }

                int offset = (_page - 1) * PageSize;
                using var cmd = Database.Cmd(conn,
                    $"SELECT * FROM NotasFiscais {where} ORDER BY Id DESC LIMIT {PageSize} OFFSET {offset}");
                if (!string.IsNullOrEmpty(_filtro)) cmd.Parameters.AddWithValue("@q", $"%{_filtro}%");
                if (!string.IsNullOrEmpty(statusTag)) cmd.Parameters.AddWithValue("@st", statusTag);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(MapRow(r));

                GridNotas.ItemsSource = list;
                if (LblPageInfo != null)
                    LblPageInfo.Text = $"Pág {_page}/{_totalPages}";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        /// <summary>Carrega uma única nota completa do banco (todos os campos, incluindo os de emissão
        /// já confirmada — ChaveAcesso, QrCodeUrl etc.) para abrir a janela de ações fiscais.</summary>
        private static NotaFiscalModel? CarregarNotaPorId(long id)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = Database.Cmd(conn, "SELECT * FROM NotasFiscais WHERE Id=@id");
                cmd.Parameters.AddWithValue("@id", id);
                using var r = cmd.ExecuteReader();
                return r.Read() ? MapRow(r) : null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar a nota: {ex.Message}", "Nota Fiscal", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        /// <summary>Linha → modelo com os itens: coluna JSON quando existe, colunas antigas quando não.</summary>
        private static NotaFiscalModel MapRow(NpgsqlDataReader r)
        {
            var nota = MapRowCampos(r);
            nota.CarregarItens(Col(r, "ItensJson"));
            return nota;
        }

        /// <summary>Mapeamento único linha→modelo, reaproveitado pela grade (<see cref="LoadGrid"/>) e
        /// pelo carregamento individual (<see cref="CarregarNotaPorId"/>) — evita duplicar ~70 linhas.</summary>
        private static NotaFiscalModel MapRowCampos(NpgsqlDataReader r) => new()
        {
            Id = Convert.ToInt64(Database.FieldOrDbNull(r, "Id")),
            Serie = Col(r, "Serie"),
            Numero = Database.FieldOrDbNull(r, "Numero") != DBNull.Value ? Convert.ToInt64(Database.FieldOrDbNull(r, "Numero")) : 0,
            DataEmissao = LerDataHora(r, "DataEmissao", DateTime.Now),
            DestNome = Col(r, "DestNome"),
            DestCpfCnpj = Col(r, "DestCpfCnpj"),
            DestIe = Col(r, "DestIe"),
            DestEmail = Col(r, "DestEmail"),
            DestLogradouro = Col(r, "DestLogradouro"),
            DestNumero = Col(r, "DestNumero"),
            DestBairro = Col(r, "DestBairro"),
            DestMunicipio = Col(r, "DestMunicipio"),
            DestUf = Col(r, "DestUf"),
            DestCep = Col(r, "DestCep"),
            DestCodigoIbge = Col(r, "DestCodigoIbge"),
            NaturezaOperacao = Col(r, "NaturezaOperacao"),
            ProdutoCodigo = Col(r, "ProdutoCodigo"),
            ProdutoDescricao = Col(r, "ProdutoDescricao"),
            ProdutoNcm = Col(r, "ProdutoNcm"),
            ProdutoCfop = Col(r, "ProdutoCfop"),
            ProdutoUnidade = Col(r, "ProdutoUnidade"),
            ProdutoQuantidade = DbDec(Database.FieldOrDbNull(r, "ProdutoQuantidade")),
            ProdutoValorUnitario = DbDec(Database.FieldOrDbNull(r, "ProdutoValorUnitario")),
            ProdutoValorTotal = DbDec(Database.FieldOrDbNull(r, "ProdutoValorTotal")),
            IcmsAliquota = DbDec(Database.FieldOrDbNull(r, "IcmsAliquota")),
            IcmsValor = DbDec(Database.FieldOrDbNull(r, "IcmsValor")),
            PisAliquota = DbDec(Database.FieldOrDbNull(r, "PisAliquota")),
            PisValor = DbDec(Database.FieldOrDbNull(r, "PisValor")),
            CofinsAliquota = DbDec(Database.FieldOrDbNull(r, "CofinsAliquota")),
            CofinsValor = DbDec(Database.FieldOrDbNull(r, "CofinsValor")),
            ValorProdutos = DbDec(Database.FieldOrDbNull(r, "ValorProdutos")),
            ValorFrete = DbDec(Database.FieldOrDbNull(r, "ValorFrete")),
            ValorDesconto = DbDec(Database.FieldOrDbNull(r, "ValorDesconto")),
            ValorTotalNota = DbDec(Database.FieldOrDbNull(r, "ValorTotalNota")),
            FormaPagamento = Col(r, "FormaPagamento", "01"),
            InformacoesComplementares = Col(r, "InformacoesComplementares"),
            Status = Col(r, "Status", "Rascunho"),
            CaminhoXml = Col(r, "CaminhoXml"),
            CstIbsCbs = Col(r, "CstIbsCbs", "000"),
            ClassTrib = Col(r, "ClassTrib", "000001"),
            CbsAliquota = DbDec(SafeCol(r, "CbsAliquota")),
            CbsValor = DbDec(SafeCol(r, "CbsValor")),
            IbsAliquota = DbDec(SafeCol(r, "IbsAliquota")),
            IbsValor = DbDec(SafeCol(r, "IbsValor")),
            IbsAliquotaUf = DbDec(SafeCol(r, "IbsAliquotaUf")),
            IbsValorUf = DbDec(SafeCol(r, "IbsValorUf")),
            IbsAliquotaMun = DbDec(SafeCol(r, "IbsAliquotaMun")),
            IbsValorMun = DbDec(SafeCol(r, "IbsValorMun")),
            IdDest = Col(r, "IdDest", "1"),
            IndIEDest = Col(r, "IndIEDest", "9"),
            Csosn = Col(r, "Csosn", "102"),
            ProdutoCest = Col(r, "ProdutoCest"),
            ProdutoGtin = Col(r, "ProdutoGtin", "SEM GTIN"),
            IcmsOrigem = Col(r, "IcmsOrigem", "0"),
            IcmsCst = Col(r, "IcmsCst", "00"),
            PisCst = Col(r, "PisCst", "01"),
            CofinsCst = Col(r, "CofinsCst", "01"),
            TipoOperacao = Col(r, "TipoOperacao", "1"),
            Finalidade = Col(r, "Finalidade", "1"),
            ConsumidorFinal = Col(r, "ConsumidorFinal", "1"),
            PresencaComprador = Col(r, "PresencaComprador", "1"),
            Ambiente = Col(r, "Ambiente", "2"),
            Modelo = Col(r, "Modelo", "55"),
            ChaveAcesso = Col(r, "ChaveAcesso"),
            NProt = Col(r, "NProt"),
            DhRecbto = Col(r, "DhRecbto"),
            CStat = Col(r, "CStat"),
            XMotivo = Col(r, "XMotivo"),
            MensagemTraduzida = Col(r, "MensagemTraduzida"),
            QrCodeUrl = Col(r, "QrCodeUrl"),
            XmlAutorizado = Col(r, "XmlAutorizado")
        };

        private static object? SafeCol(NpgsqlDataReader r, string name)
        {
            try { return Database.FieldOrDbNull(r, name); }
            catch { return DBNull.Value; }
        }

        private static void AtualizarUltimoNumero(long numero) =>
            EmpresaConfigStore.AtualizarUltimoNumeroNfeSeMaior(numero);

        private static decimal ParseDec(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Replace("R$", "").Trim();
            if (decimal.TryParse(s, NumberStyles.Number, PtBr, out var v)) return v;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v2)) return v2;
            return 0;
        }

        private static decimal DbDec(object? o)
        {
            if (o == null || o == DBNull.Value) return 0;
            if (o is decimal d) return d;
            if (o is double dbl) return Convert.ToDecimal(dbl);
            return ParseDec(o.ToString());
        }

        private static string Col(NpgsqlDataReader r, string c, string def = "")
        {
            try { var v = r[c]; return v == DBNull.Value || v == null ? def : v.ToString() ?? def; }
            catch { return def; }
        }

        /// <summary>
        /// Lê data/hora aceitando tanto a coluna já tipada (TIMESTAMP) quanto o texto ISO legado —
        /// bases anteriores à migração podem ter ficado como TEXT.
        /// </summary>
        private static DateTime LerDataHora(NpgsqlDataReader r, string coluna, DateTime padrao)
        {
            object? v = Database.Field(r, coluna);
            if (v is DateTime dt) return dt;

            string s = v?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(s)) return padrao;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)) return iso;
            return DateTime.TryParse(s, PtBr, DateTimeStyles.None, out var br) ? br : padrao;
        }
    }
}
