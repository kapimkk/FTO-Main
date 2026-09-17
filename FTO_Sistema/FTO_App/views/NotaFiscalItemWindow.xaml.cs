using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FTO_App.Models;
using FTO_App.Services;

namespace FTO_App.Views
{
    /// <summary>
    /// Editor de um item da NF-e.
    ///
    /// No modo "adicionar" o botão padrão (Enter) é "Salvar e adicionar outro": o item vai direto
    /// para a nota e o formulário volta limpo, mantendo CFOP, unidade e tributação do item anterior
    /// — lançar vários produtos parecidos em sequência não exige reabrir a janela nem redigitar os
    /// mesmos códigos fiscais.
    /// </summary>
    public partial class NotaFiscalItemWindow : Window
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        private readonly Action<NotaFiscalItemModel>? _aoAdicionarEmSequencia;
        private readonly bool _modoEdicao;
        private readonly bool _regimeNormal;
        private int _numeroItem;
        private int _adicionadosEmSequencia;
        private long? _produtoId;
        private bool _carregando;

        private CancellationTokenSource? _ncmCts;
        private bool _suprimirBuscaNcm;

        /// <summary>Item salvo pelo botão principal (nulo se fechou sem salvar por ele).</summary>
        public NotaFiscalItemModel? Resultado { get; private set; }

        /// <param name="item">Item a editar; nulo abre em modo "adicionar".</param>
        /// <param name="numeroItem">Posição que o item terá (ou tem) na nota — só para exibição.</param>
        /// <param name="aoAdicionarEmSequencia">
        /// Recebe cada item salvo por "Salvar e adicionar outro". Sem ele, o botão some.
        /// </param>
        /// <param name="modelo">Item de onde herdar CFOP, unidade e tributação num item novo.</param>
        public NotaFiscalItemWindow(
            NotaFiscalItemModel? item,
            int numeroItem,
            Action<NotaFiscalItemModel>? aoAdicionarEmSequencia = null,
            NotaFiscalItemModel? modelo = null)
        {
            InitializeComponent();

            _modoEdicao = item != null;
            _numeroItem = numeroItem;
            _aoAdicionarEmSequencia = aoAdicionarEmSequencia;
            _regimeNormal = EmpresaConfigStore.Current.RegimeTributario == "3";

            ConfigurarRegime();

            _carregando = true;
            if (item != null)
            {
                CarregarItem(item);
                ChkAutoIbsCbs.IsChecked = false; // mantém o que já foi gravado no item
            }
            else
            {
                AplicarModelo(modelo);
                ChkAutoIbsCbs.IsChecked = EmpresaConfigStore.Current.IbsCbsCalculoAutomatico;
            }
            _carregando = false;

            ConfigurarModo();
            Recalcular();

            Loaded += (_, _) =>
            {
                // Veio do estoque: código, preço e tributos já estão lá — o que falta é a quantidade.
                TextBox foco = !_modoEdicao && _produtoId != null ? TxtQuantidade : TxtDescricao;
                foco.Focus();
                foco.SelectAll();
            };
        }

        // -----------------------------------------------------------------
        // Configuração da tela
        // -----------------------------------------------------------------

        private void ConfigurarRegime()
        {
            PanelCst.Visibility = _regimeNormal ? Visibility.Visible : Visibility.Collapsed;
            PanelCsosn.Visibility = _regimeNormal ? Visibility.Collapsed : Visibility.Visible;

            if (_regimeNormal)
            {
                LblRegime.Text = "Regime normal (CRT 3) — ICMS destacado por CST.";
            }
            else
            {
                LblRegime.Text = "Simples Nacional — ICMS por CSOSN. A alíquota só gera crédito no CSOSN 101.";
                LblIcmsAliq.Text = "Crédito SN % (CSOSN 101)";
                LblIcmsValor.Text = "Valor do crédito";
            }
        }

        private void ConfigurarModo()
        {
            if (_modoEdicao)
            {
                LblTitulo.Text = "Editar item";
                BtnSalvar.Content = "✓  Salvar alterações";
                BtnSalvar.IsDefault = true;
                BtnSalvarEOutro.Visibility = Visibility.Collapsed;
            }
            else
            {
                LblTitulo.Text = "Adicionar item";
                bool sequencia = _aoAdicionarEmSequencia != null;
                BtnSalvarEOutro.Visibility = sequencia ? Visibility.Visible : Visibility.Collapsed;
                BtnSalvarEOutro.IsDefault = sequencia;
                BtnSalvar.IsDefault = !sequencia;
            }
            AtualizarSubtitulo();
        }

        private void AtualizarSubtitulo()
        {
            LblSubtitulo.Text = _modoEdicao || _aoAdicionarEmSequencia == null
                ? $"Item {_numeroItem} da nota"
                : $"Item {_numeroItem} da nota · Enter salva e já abre o próximo";
        }

        // -----------------------------------------------------------------
        // Campos ⇄ modelo
        // -----------------------------------------------------------------

        private void CarregarItem(NotaFiscalItemModel i)
        {
            _produtoId = i.ProdutoId;
            TxtCodigo.Text = i.Codigo;
            TxtDescricao.Text = i.Descricao;
            _suprimirBuscaNcm = true;
            TxtNcm.Text = i.Ncm;
            _suprimirBuscaNcm = false;
            TxtCest.Text = i.Cest;
            TxtGtin.Text = string.IsNullOrWhiteSpace(i.Gtin) ? "SEM GTIN" : i.Gtin;
            TxtCfop.Text = i.Cfop;
            CbUnidade.Text = string.IsNullOrWhiteSpace(i.Unidade) ? "UN" : i.Unidade;
            TxtQuantidade.Text = i.Quantidade.ToString("0.####", PtBr);
            TxtValorUnitario.Text = i.ValorUnitario.ToString("0.00##", PtBr);

            CarregarTributacao(i);

            TxtCbsAliq.Text = i.CbsAliquota.ToString("0.####", PtBr);
            TxtIbsAliq.Text = i.IbsAliquota.ToString("0.####", PtBr);
        }

        /// <summary>CFOP, unidade e tributos do item-modelo; campos do produto em branco.</summary>
        private void AplicarModelo(NotaFiscalItemModel? modelo)
        {
            _produtoId = null;
            TxtCodigo.Text = "";
            TxtDescricao.Text = "";
            _suprimirBuscaNcm = true;
            TxtNcm.Text = "";
            _suprimirBuscaNcm = false;
            TxtCest.Text = "";
            TxtGtin.Text = "SEM GTIN";
            TxtQuantidade.Text = "1";
            TxtValorUnitario.Text = "";

            if (modelo == null)
            {
                TxtCfop.Text = "5102";
                CbUnidade.Text = "UN";
                SetComboTag(CbOrigem, "0");
                CbCstIcms.Text = "00";
                CbCsosn.Text = "102";
                TxtIcmsAliq.Text = TxtPisAliq.Text = TxtCofinsAliq.Text = "0";
                CbPisCst.Text = CbCofinsCst.Text = "01";
                TxtCstIbsCbs.Text = ReformaTributariaService.CstPadrao;
                TxtClassTrib.Text = ReformaTributariaService.ClassTribPadrao;
                return;
            }

            TxtCfop.Text = modelo.Cfop;
            CbUnidade.Text = string.IsNullOrWhiteSpace(modelo.Unidade) ? "UN" : modelo.Unidade;
            CarregarTributacao(modelo);
        }

        private void CarregarTributacao(NotaFiscalItemModel i)
        {
            SetComboTag(CbOrigem, string.IsNullOrWhiteSpace(i.IcmsOrigem) ? "0" : i.IcmsOrigem);
            CbCstIcms.Text = string.IsNullOrWhiteSpace(i.IcmsCst) ? "00" : i.IcmsCst;
            CbCsosn.Text = string.IsNullOrWhiteSpace(i.Csosn) ? "102" : i.Csosn;
            TxtIcmsAliq.Text = i.IcmsAliquota.ToString("0.####", PtBr);
            CbPisCst.Text = string.IsNullOrWhiteSpace(i.PisCst) ? "01" : i.PisCst;
            TxtPisAliq.Text = i.PisAliquota.ToString("0.####", PtBr);
            CbCofinsCst.Text = string.IsNullOrWhiteSpace(i.CofinsCst) ? "01" : i.CofinsCst;
            TxtCofinsAliq.Text = i.CofinsAliquota.ToString("0.####", PtBr);
            TxtCstIbsCbs.Text = string.IsNullOrWhiteSpace(i.CstIbsCbs) ? ReformaTributariaService.CstPadrao : i.CstIbsCbs;
            TxtClassTrib.Text = string.IsNullOrWhiteSpace(i.ClassTrib) ? ReformaTributariaService.ClassTribPadrao : i.ClassTrib;
        }

        private NotaFiscalItemModel MontarItem()
        {
            var item = new NotaFiscalItemModel
            {
                ProdutoId = _produtoId,
                Codigo = TxtCodigo.Text.Trim(),
                Descricao = TxtDescricao.Text.Trim(),
                Ncm = ReformaTributariaService.NormalizarNcm(TxtNcm.Text),
                Cest = DocumentValidator.OnlyDigits(TxtCest.Text),
                Gtin = string.IsNullOrWhiteSpace(TxtGtin.Text) ? "SEM GTIN" : TxtGtin.Text.Trim(),
                Cfop = DocumentValidator.OnlyDigits(TxtCfop.Text),
                Unidade = (CbUnidade.Text ?? "").Trim().ToUpperInvariant(),
                Quantidade = ParseDec(TxtQuantidade.Text),
                ValorUnitario = ParseDec(TxtValorUnitario.Text),
                IcmsOrigem = GetComboTag(CbOrigem, "0"),
                IcmsCst = TextoOu(CbCstIcms.Text, "00"),
                Csosn = TextoOu(CbCsosn.Text, "102"),
                IcmsAliquota = ParseDec(TxtIcmsAliq.Text),
                PisCst = TextoOu(CbPisCst.Text, "01"),
                PisAliquota = ParseDec(TxtPisAliq.Text),
                CofinsCst = TextoOu(CbCofinsCst.Text, "01"),
                CofinsAliquota = ParseDec(TxtCofinsAliq.Text),
                CstIbsCbs = ReformaTributariaService.NormalizarCst(TxtCstIbsCbs.Text),
                ClassTrib = ReformaTributariaService.NormalizarClassTrib(TxtClassTrib.Text)
            };
            item.Recalcular();

            var ibs = CalcularIbsCbs(item.ValorTotal);
            item.CbsAliquota = ibs.CbsAliq;
            item.CbsValor = ibs.CbsValor;
            item.IbsAliquota = ibs.IbsAliq;
            item.IbsValor = ibs.IbsValor;
            item.IbsAliquotaUf = ibs.UfAliq;
            item.IbsValorUf = ibs.UfValor;
            item.IbsAliquotaMun = ibs.MunAliq;
            item.IbsValorMun = ibs.MunValor;
            return item;
        }

        // -----------------------------------------------------------------
        // Cálculo ao vivo
        // -----------------------------------------------------------------

        private void Valores_TextChanged(object sender, TextChangedEventArgs e) => Recalcular();
        private void ChkAutoIbsCbs_Click(object sender, RoutedEventArgs e) => Recalcular();

        private void Recalcular()
        {
            if (_carregando || TxtValorTotal == null || TxtIbsValor == null) return;

            decimal total = Math.Round(ParseDec(TxtQuantidade.Text) * ParseDec(TxtValorUnitario.Text), 2);
            decimal icms = Math.Round(total * ParseDec(TxtIcmsAliq.Text) / 100m, 2);

            TxtValorTotal.Text = total.ToString("C2", PtBr);
            TxtIcmsValor.Text = icms.ToString("C2", PtBr);
            LblTotalRodape.Text = total.ToString("C2", PtBr);

            var cfg = EmpresaConfigStore.Current;
            LblIbsCbsInfo.Text = ReformaTributariaService.DescricaoPreset(cfg.IbsCbsPreset);

            bool auto = ChkAutoIbsCbs.IsChecked == true;
            TxtCbsAliq.IsEnabled = TxtIbsAliq.IsEnabled = !auto;

            var r = CalcularIbsCbs(total);
            if (auto)
            {
                _carregando = true;
                TxtCbsAliq.Text = r.CbsAliq.ToString("0.####", PtBr);
                TxtIbsAliq.Text = r.IbsAliq.ToString("0.####", PtBr);
                _carregando = false;
            }
            TxtCbsValor.Text = r.CbsValor.ToString("C2", PtBr);
            TxtIbsValor.Text = r.IbsValor.ToString("C2", PtBr);
        }

        private (decimal CbsAliq, decimal CbsValor, decimal IbsAliq, decimal IbsValor,
                 decimal UfAliq, decimal UfValor, decimal MunAliq, decimal MunValor) CalcularIbsCbs(decimal total)
        {
            var cfg = EmpresaConfigStore.Current;

            if (ChkAutoIbsCbs.IsChecked == true)
            {
                var r = ReformaTributariaService.Calcular(total, cfg);
                return (r.AliquotaCbs, r.ValorCbs, r.AliquotaIbs, r.ValorIbs,
                        r.AliquotaIbsUf, r.ValorIbsUf, r.AliquotaIbsMun, r.ValorIbsMun);
            }

            decimal cbsA = ParseDec(TxtCbsAliq.Text);
            decimal ibsA = ParseDec(TxtIbsAliq.Text);
            var (_, _, ufPreset, munPreset) = ReformaTributariaService.AliquotasDoPreset(cfg.IbsCbsPreset, cfg);

            // Mantém o rateio UF/Município do preset quando só o IBS total foi alterado.
            decimal fatorUf = (ufPreset + munPreset) > 0 ? ufPreset / (ufPreset + munPreset) : 0.5m;
            decimal ibsV = Math.Round(total * ibsA / 100m, 2);
            decimal ufV = Math.Round(ibsV * fatorUf, 2);
            decimal ufA = Math.Round(ibsA * fatorUf, 4);

            return (cbsA, Math.Round(total * cbsA / 100m, 2), ibsA, ibsV,
                    ufA, ufV, ibsA - ufA, ibsV - ufV);
        }

        // -----------------------------------------------------------------
        // Ações
        // -----------------------------------------------------------------

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            // Já lançou itens em sequência e deixou o formulário em branco: só fechar.
            if (!_modoEdicao && _adicionadosEmSequencia > 0 && FormularioEmBranco())
            {
                DialogResult = true;
                return;
            }

            var item = MontarItem();
            if (!Validar(item)) return;

            Resultado = item;
            DialogResult = true;
        }

        private void BtnSalvarEOutro_Click(object sender, RoutedEventArgs e)
        {
            if (_aoAdicionarEmSequencia == null) return;

            var item = MontarItem();
            if (!Validar(item)) return;

            _aoAdicionarEmSequencia(item);
            _adicionadosEmSequencia++;
            _numeroItem++;

            PainelAdicionados.Visibility = Visibility.Visible;
            LblAdicionados.Text = _adicionadosEmSequencia == 1
                ? $"✓  \"{item.Descricao}\" adicionado à nota. Preencha o próximo item ou clique em Cancelar para voltar."
                : $"✓  \"{item.Descricao}\" adicionado — {_adicionadosEmSequencia} itens lançados nesta sequência.";

            _carregando = true;
            AplicarModelo(item);
            _carregando = false;

            BtnSalvar.Content = "✓  Adicionar e concluir";
            AtualizarSubtitulo();
            Recalcular();
            TxtDescricao.Focus();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            // Itens lançados em sequência já estão na nota; Cancelar só descarta o formulário atual.
            DialogResult = _adicionadosEmSequencia > 0;
        }

        private bool FormularioEmBranco() =>
            string.IsNullOrWhiteSpace(TxtDescricao.Text) &&
            string.IsNullOrWhiteSpace(TxtNcm.Text) &&
            ParseDec(TxtValorUnitario.Text) <= 0;

        private bool Validar(NotaFiscalItemModel item)
        {
            var erros = NotaFiscalValidacao.ValidarItem(item);
            if (erros.Count == 0) return true;

            MessageBox.Show("Revise o item:\n\n• " + string.Join("\n• ", erros),
                "Item da NF-e", MessageBoxButton.OK, MessageBoxImage.Warning);

            // Leva o cursor ao primeiro campo com problema.
            string primeiro = erros[0];
            Control alvo = primeiro.StartsWith("descrição") ? TxtDescricao
                : primeiro.StartsWith("NCM") ? TxtNcm
                : primeiro.StartsWith("CFOP") ? TxtCfop
                : primeiro.StartsWith("unidade") ? CbUnidade
                : primeiro.StartsWith("quantidade") ? TxtQuantidade
                : TxtValorUnitario;
            alvo.Focus();
            return false;
        }

        // -----------------------------------------------------------------
        // Produto do estoque
        // -----------------------------------------------------------------

        private void BtnDoEstoque_Click(object sender, RoutedEventArgs e)
        {
            var win = new ProdutoEstoquePickerWindow { Owner = this };
            if (win.ShowDialog() != true || win.ProdutoSelecionado is null) return;
            AplicarProdutoDoEstoque(win.ProdutoSelecionado);
        }

        /// <summary>Abre a janela já pedindo o produto do estoque (atalho da tela da nota).</summary>
        public bool EscolherDoEstoque()
        {
            var win = new ProdutoEstoquePickerWindow { Owner = Owner };
            if (win.ShowDialog() != true || win.ProdutoSelecionado is null) return false;
            return AplicarProdutoDoEstoque(win.ProdutoSelecionado);
        }

        private bool AplicarProdutoDoEstoque(ProdutoModel p)
        {
            var faltando = new List<string>();
            if (string.IsNullOrWhiteSpace(p.Nome) && string.IsNullOrWhiteSpace(p.Descricao))
                faltando.Add("nome/descrição");
            if (DocumentValidator.OnlyDigits(p.Ncm).Length != 8)
                faltando.Add("NCM (8 dígitos)");
            if (DocumentValidator.OnlyDigits(p.Cfop).Length != 4)
                faltando.Add("CFOP (4 dígitos)");
            if (p.PrecoVenda <= 0)
                faltando.Add("preço de venda");
            if (p.Quantidade <= 0)
                faltando.Add("quantidade em estoque");
            if (_regimeNormal && string.IsNullOrWhiteSpace(p.CstIcms))
                faltando.Add("CST ICMS");
            if (!_regimeNormal && string.IsNullOrWhiteSpace(p.Csosn))
                faltando.Add("CSOSN");

            if (faltando.Count > 0)
            {
                MessageBox.Show(
                    "O produto do estoque está incompleto para emitir nota. Complete no módulo Estoque:\n\n• " +
                    string.Join("\n• ", faltando),
                    "Produto incompleto", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            _carregando = true;
            _produtoId = p.Id > 0 ? p.Id : null;
            TxtCodigo.Text = !string.IsNullOrWhiteSpace(p.CodigoBarras) ? p.CodigoBarras.Trim()
                : p.Id > 0 ? p.Id.ToString(PtBr) : "";
            TxtDescricao.Text = !string.IsNullOrWhiteSpace(p.Descricao) ? p.Descricao.Trim() : p.Nome.Trim();
            _suprimirBuscaNcm = true;
            TxtNcm.Text = DocumentValidator.OnlyDigits(p.Ncm);
            _suprimirBuscaNcm = false;
            TxtCest.Text = (p.Cest ?? "").Trim();
            TxtGtin.Text = !string.IsNullOrWhiteSpace(p.CodigoBarras) && p.CodigoBarras.Trim().Length is >= 8 and <= 14
                ? p.CodigoBarras.Trim()
                : "SEM GTIN";
            TxtCfop.Text = DocumentValidator.OnlyDigits(p.Cfop);
            CbUnidade.Text = string.IsNullOrWhiteSpace(p.Unidade) ? "UN" : p.Unidade.Trim();
            TxtQuantidade.Text = "1";
            TxtValorUnitario.Text = p.PrecoVenda.ToString("0.00##", PtBr);

            SetComboTag(CbOrigem, string.IsNullOrWhiteSpace(p.Origem) ? "0" : p.Origem.Trim());
            if (!string.IsNullOrWhiteSpace(p.CstIcms)) CbCstIcms.Text = p.CstIcms.Trim();
            if (!string.IsNullOrWhiteSpace(p.Csosn)) CbCsosn.Text = p.Csosn.Trim();
            TxtIcmsAliq.Text = p.IcmsAliquota.ToString("0.####", PtBr);
            if (!string.IsNullOrWhiteSpace(p.PisCst)) CbPisCst.Text = p.PisCst.Trim();
            TxtPisAliq.Text = p.PisAliquota.ToString("0.####", PtBr);
            if (!string.IsNullOrWhiteSpace(p.CofinsCst)) CbCofinsCst.Text = p.CofinsCst.Trim();
            TxtCofinsAliq.Text = p.CofinsAliquota.ToString("0.####", PtBr);
            if (!string.IsNullOrWhiteSpace(p.CstIbsCbs)) TxtCstIbsCbs.Text = p.CstIbsCbs.Trim();
            if (!string.IsNullOrWhiteSpace(p.ClassTrib)) TxtClassTrib.Text = p.ClassTrib.Trim();
            if (p.CbsAliquota > 0 || p.IbsAliquota > 0)
            {
                ChkAutoIbsCbs.IsChecked = false;
                TxtCbsAliq.Text = p.CbsAliquota.ToString("0.####", PtBr);
                TxtIbsAliq.Text = p.IbsAliquota.ToString("0.####", PtBr);
            }
            _carregando = false;

            Recalcular();
            // Preço e código já vieram; o que normalmente muda é a quantidade.
            TxtQuantidade.Focus();
            TxtQuantidade.SelectAll();
            return true;
        }

        // -----------------------------------------------------------------
        // Autocomplete de NCM (BrasilAPI) — debounce de ~350ms, mínimo 3
        // caracteres; falha de rede não bloqueia (usuário digita manualmente).
        // -----------------------------------------------------------------

        private async void TxtNcm_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suprimirBuscaNcm || _carregando || !IsLoaded) return;

            _ncmCts?.Cancel();
            var cts = new CancellationTokenSource();
            _ncmCts = cts;
            string termo = TxtNcm.Text;

            try { await Task.Delay(350, cts.Token); }
            catch (TaskCanceledException) { return; }
            if (cts.IsCancellationRequested) return;

            var sugestoes = await NcmService.BuscarAsync(termo);
            if (cts.IsCancellationRequested) return;

            if (sugestoes.Count == 0)
            {
                PopupNcm.IsOpen = false;
                return;
            }
            ListNcm.ItemsSource = sugestoes;
            PopupNcm.IsOpen = true;
        }

        private void ListNcm_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ListNcm.SelectedItem is NcmResult ncm)
            {
                _suprimirBuscaNcm = true;
                TxtNcm.Text = ReformaTributariaService.NormalizarNcm(ncm.Codigo);
                TxtNcm.CaretIndex = TxtNcm.Text.Length;
                _suprimirBuscaNcm = false;

                if (string.IsNullOrWhiteSpace(TxtDescricao.Text))
                    TxtDescricao.Text = ncm.Descricao;
            }
            PopupNcm.IsOpen = false;
        }

        // -----------------------------------------------------------------
        // Utilitários
        // -----------------------------------------------------------------

        private static string TextoOu(string? texto, string padrao) =>
            string.IsNullOrWhiteSpace(texto) ? padrao : texto.Trim();

        private static void SetComboTag(ComboBox cb, string tag)
        {
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem it &&
                    string.Equals(it.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }
        }

        private static string GetComboTag(ComboBox cb, string fallback) =>
            cb.SelectedItem is ComboBoxItem it && it.Tag != null ? it.Tag.ToString() ?? fallback : fallback;

        private static decimal ParseDec(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Replace("R$", "").Replace("%", "").Trim();
            if (decimal.TryParse(s, NumberStyles.Number, PtBr, out var v)) return v;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v2)) return v2;
            return 0;
        }
    }
}
