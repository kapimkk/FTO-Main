using FTO_App.Models;
using FTO_App.Services;
using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using Npgsql;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FTO_App.Views
{
    public partial class ClientesView : UserControl
    {
        private const int PageSize = 50;
        private long? _editingId;
        private string _filtro = "";
        private int _page = 1;
        private int _totalPages = 1;
        private bool _buscandoCep;
        private bool _buscandoDocumento;

        public ClientesView()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadGrid();
        }

        private void BtnNovo_Click(object sender, RoutedEventArgs e)
        {
            LimparForm();
            LblFormTitulo.Text = "Cadastrar cliente";
            FormOverlay.Visibility = Visibility.Visible;
            TxtNome.Focus();
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if (GridClientes.SelectedItem is not ClienteModel c)
            {
                MessageBox.Show("Selecione um cliente na lista.", "Clientes", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            AbrirEdicao(c);
        }

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridClientes.SelectedItem is ClienteModel c)
                AbrirEdicao(c);
        }

        private void AbrirEdicao(ClienteModel c)
        {
            _editingId = c.Id;
            CbTipoPessoa.SelectedIndex = c.TipoPessoa == "J" ? 1 : 0;
            TxtNome.Text = c.Nome;
            TxtRazaoSocial.Text = c.RazaoSocial;
            TxtNomeFantasia.Text = c.NomeFantasia;
            TxtCpfCnpj.Text = c.CpfCnpj;
            AtualizarStatusDocumento(quiet: true);
            TxtIe.Text = c.Ie;
            TxtIm.Text = c.Im;
            CbIndicadorIe.SelectedIndex = c.IndicadorIe switch { "1" => 0, "2" => 1, _ => 2 };
            TxtContato.Text = c.Contato;
            TxtEmail.Text = c.Email;
            TxtCep.Text = c.Cep;
            TxtLogradouro.Text = c.Logradouro;
            TxtNumero.Text = c.Numero;
            TxtComplemento.Text = c.Complemento;
            TxtBairro.Text = c.Bairro;
            TxtMunicipio.Text = c.Municipio;
            TxtUf.Text = c.Uf;
            TxtCodigoIbge.Text = c.CodigoIbge;
            TxtPais.Text = string.IsNullOrWhiteSpace(c.Pais) ? "Brasil" : c.Pais;
            TxtCodigoPais.Text = string.IsNullOrWhiteSpace(c.CodigoPais) ? "1058" : c.CodigoPais;
            LblFormTitulo.Text = "Editar cliente";
            BtnSalvar.Content = "💾 ATUALIZAR";
            FormOverlay.Visibility = Visibility.Visible;
        }

        private void BtnFecharForm_Click(object sender, RoutedEventArgs e)
        {
            FormOverlay.Visibility = Visibility.Collapsed;
            LimparForm();
        }

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNome.Text))
            {
                MessageBox.Show("Nome é obrigatório.", "Clientes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // CPF/CNPJ é opcional no cadastro — cliente de balcão muitas vezes não informa.
            // Quem exige é a emissão: a NF-e/NFS-e cobra o documento do destinatário/tomador lá.
            // Mas, se foi digitado, precisa ser válido: documento errado gravado é pior que vazio.
            string tipo;
            string docFmt;
            if (DocumentValidator.OnlyDigits(TxtCpfCnpj.Text).Length == 0)
            {
                tipo = (CbTipoPessoa.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "J" ? "J" : "F";
                docFmt = "";
                TxtCpfCnpj.Text = "";
            }
            else
            {
                if (!DocumentValidator.TryValidate(TxtCpfCnpj.Text, out tipo, out docFmt, out string erroDoc))
                {
                    MessageBox.Show(erroDoc + "\n\nCorrija ou deixe o campo em branco — o CPF/CNPJ é opcional.",
                        "CPF/CNPJ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtCpfCnpj.Focus();
                    return;
                }
                TxtCpfCnpj.Text = docFmt;
                CbTipoPessoa.SelectedIndex = tipo == "J" ? 1 : 0;
            }
            string indIe = (CbIndicadorIe.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "9";

            var p = new Dictionary<string, object>
            {
                ["@tp"] = tipo,
                ["@no"] = TxtNome.Text.Trim(),
                ["@rs"] = Db(TxtRazaoSocial.Text),
                ["@nf"] = Db(TxtNomeFantasia.Text),
                ["@co"] = Db(TxtContato.Text),
                ["@em"] = Db(TxtEmail.Text),
                // Vazio vai como NULL, não '' — coluna sem valor fica sem valor.
                ["@cp"] = Db(docFmt),
                ["@ie"] = Db(TxtIe.Text),
                ["@im"] = Db(TxtIm.Text),
                ["@ii"] = indIe,
                ["@ce"] = Db(TxtCep.Text),
                ["@lg"] = Db(TxtLogradouro.Text),
                ["@nu"] = Db(TxtNumero.Text),
                ["@cm"] = Db(TxtComplemento.Text),
                ["@ba"] = Db(TxtBairro.Text),
                ["@mu"] = Db(TxtMunicipio.Text),
                ["@uf"] = Db(TxtUf.Text?.ToUpperInvariant()),
                ["@ib"] = Db(TxtCodigoIbge.Text),
                ["@pa"] = string.IsNullOrWhiteSpace(TxtPais.Text) ? "Brasil" : TxtPais.Text.Trim(),
                ["@cpais"] = string.IsNullOrWhiteSpace(TxtCodigoPais.Text) ? "1058" : TxtCodigoPais.Text.Trim(),
            };

            try
            {
                if (_editingId.HasValue)
                {
                    p["@id"] = _editingId.Value;
                    Database.ExecuteNonQuery(@"UPDATE Clientes SET TipoPessoa=@tp, Nome=@no, RazaoSocial=@rs, NomeFantasia=@nf,
                        Contato=@co, Email=@em, Cpf_Cnpj=@cp, Ie=@ie, Im=@im, IndicadorIe=@ii, Cep=@ce, Logradouro=@lg,
                        Numero=@nu, Complemento=@cm, Bairro=@ba, Municipio=@mu, Uf=@uf, CodigoIbge=@ib, Pais=@pa, CodigoPais=@cpais
                        WHERE Id=@id", p);
                }
                else
                {
                    Database.ExecuteNonQuery(@"INSERT INTO Clientes
                        (TipoPessoa, Nome, RazaoSocial, NomeFantasia, Contato, Email, Cpf_Cnpj, Ie, Im, IndicadorIe,
                         Cep, Logradouro, Numero, Complemento, Bairro, Municipio, Uf, CodigoIbge, Pais, CodigoPais, Ativo)
                        VALUES (@tp,@no,@rs,@nf,@co,@em,@cp,@ie,@im,@ii,@ce,@lg,@nu,@cm,@ba,@mu,@uf,@ib,@pa,@cpais,1)", p);
                }

                MessageBox.Show("Cliente salvo!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                FormOverlay.Visibility = Visibility.Collapsed;
                LimparForm();
                LoadGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Clientes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExcluirLista_Click(object sender, RoutedEventArgs e)
        {
            if (GridClientes.SelectedItem is not ClienteModel c) return;
            if (MessageBox.Show($"Excluir o cliente \"{c.Nome}\"?", "Confirma", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            Database.ExecuteNonQuery("DELETE FROM Clientes WHERE Id=@id", new Dictionary<string, object> { ["@id"] = c.Id });
            LoadGrid();
        }

        private void BtnFiltrar_Click(object sender, RoutedEventArgs e)
        {
            _filtro = TxtBusca.Text.Trim();
            _page = 1;
            LoadGrid();
        }

        private void BtnBackup_Click(object sender, RoutedEventArgs e)
        {
            var list = CarregarTodosParaExport();
            if (list.Count == 0)
            {
                MessageBox.Show("Não há clientes cadastrados para fazer backup.");
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "Excel|*.xlsx",
                FileName = $"Backup_Clientes_{DateTime.Now:yyyyMMdd}.xlsx"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Clientes");
                ws.Cell(1, 1).InsertTable(list);
                ws.Columns().AdjustToContents();
                wb.SaveAs(sfd.FileName);
                MessageBox.Show("Backup de clientes realizado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao gerar o backup: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnPdf_Click(object sender, RoutedEventArgs e)
        {
            var list = CarregarTodosParaExport();
            if (list.Count == 0)
            {
                MessageBox.Show("Não há clientes cadastrados para exportar em PDF.");
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "PDF|*.pdf",
                DefaultExt = ".pdf",
                FileName = $"Clientes_FTO_{DateTime.Now:yyyyMMdd}.pdf"
            };
            if (saveDialog.ShowDialog() != true) return;

            try
            {
                PdfService.GerarListaClientesPdf(list, saveDialog.FileName);
                MessageBox.Show($"PDF gerado com sucesso!\n\n{saveDialog.FileName}", "Clientes PDF",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao gerar PDF: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<ClienteModel> CarregarTodosParaExport()
        {
            var list = new List<ClienteModel>();
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = Database.Cmd(conn, "SELECT * FROM Clientes ORDER BY Nome");
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new ClienteModel
                    {
                        Id = Convert.ToInt64(Database.FieldOrDbNull(r, "Id")),
                        TipoPessoa = Str(r, "TipoPessoa", "F"),
                        Nome = Str(r, "Nome"),
                        Contato = Str(r, "Contato"),
                        CpfCnpj = Str(r, "Cpf_Cnpj"),
                        Email = Str(r, "Email"),
                        Municipio = Str(r, "Municipio"),
                        Uf = Str(r, "Uf"),
                        CodigoIbge = Str(r, "CodigoIbge"),
                        Ativo = Database.FieldOrDbNull(r, "Ativo") != DBNull.Value ? Convert.ToInt32(Database.FieldOrDbNull(r, "Ativo")) : 1
                    });
                }
            }
            catch { }
            return list;
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

        private void TxtCpfCnpj_TextChanged(object sender, TextChangedEventArgs e) => AtualizarStatusDocumento(quiet: true);

        private async void TxtCpfCnpj_LostFocus(object sender, RoutedEventArgs e)
        {
            AtualizarStatusDocumento(quiet: false);
            string digits = DocumentValidator.OnlyDigits(TxtCpfCnpj.Text);
            if (digits.Length is 11 or 14)
                TxtCpfCnpj.Text = DocumentValidator.Format(digits);

            // Auto-consulta somente CNPJ (se nome ainda vazio)
            if (digits.Length == 14 && DocumentValidator.IsValidCnpj(digits) &&
                string.IsNullOrWhiteSpace(TxtNome.Text) && string.IsNullOrWhiteSpace(TxtRazaoSocial.Text))
                await BuscarCadastroDocumentoAsync();
        }

        private async void TxtCpfCnpj_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await BuscarCadastroDocumentoAsync();
        }

        private async void BtnBuscarDocumento_Click(object sender, RoutedEventArgs e)
            => await BuscarCadastroDocumentoAsync();

        private async System.Threading.Tasks.Task BuscarCadastroDocumentoAsync()
        {
            if (_buscandoDocumento) return;

            string digits = DocumentValidator.OnlyDigits(TxtCpfCnpj.Text);
            if (digits.Length == 11)
            {
                LblDocStatus.Text = "Consulta automática só para CNPJ — informe o nome do CPF manualmente.";
                LblDocStatus.Foreground = Brushes.DarkOrange;
                MessageBox.Show(
                    "A busca automática é apenas para CNPJ (Dados Abertos da Receita Federal).\n\nPara CPF, preencha o nome manualmente.",
                    "Consulta CNPJ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _buscandoDocumento = true;
            try
            {
                LblDocStatus.Text = "Consultando CNPJ na Receita Federal...";
                LblDocStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");

                var r = await DocumentoCadastroService.BuscarCnpjAsync(TxtCpfCnpj.Text);
                if (!r.Sucesso)
                {
                    LblDocStatus.Text = r.Erro ?? "Não foi possível consultar.";
                    LblDocStatus.Foreground = Brushes.DarkOrange;
                    if (!string.IsNullOrWhiteSpace(r.Erro))
                        MessageBox.Show(r.Erro, "Consulta CNPJ", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                CbTipoPessoa.SelectedIndex = 1;
                if (!string.IsNullOrWhiteSpace(r.Nome))
                {
                    TxtNome.Text = r.Nome;
                    TxtRazaoSocial.Text = r.Nome;
                }
                if (!string.IsNullOrWhiteSpace(r.NomeFantasia))
                    TxtNomeFantasia.Text = r.NomeFantasia;
                if (!string.IsNullOrWhiteSpace(r.Logradouro)) TxtLogradouro.Text = r.Logradouro;
                if (!string.IsNullOrWhiteSpace(r.Numero)) TxtNumero.Text = r.Numero;
                if (!string.IsNullOrWhiteSpace(r.Complemento)) TxtComplemento.Text = r.Complemento;
                if (!string.IsNullOrWhiteSpace(r.Bairro)) TxtBairro.Text = r.Bairro;
                if (!string.IsNullOrWhiteSpace(r.Municipio)) TxtMunicipio.Text = r.Municipio;
                if (!string.IsNullOrWhiteSpace(r.Uf)) TxtUf.Text = r.Uf;
                if (!string.IsNullOrWhiteSpace(r.Cep)) TxtCep.Text = r.Cep;
                if (!string.IsNullOrWhiteSpace(r.CodigoIbge)) TxtCodigoIbge.Text = r.CodigoIbge;

                digits = DocumentValidator.OnlyDigits(TxtCpfCnpj.Text);
                if (digits.Length == 14)
                    TxtCpfCnpj.Text = DocumentValidator.Format(digits);

                LblDocStatus.Text = string.IsNullOrWhiteSpace(r.Fonte)
                    ? "Cadastro preenchido via CNPJ"
                    : $"Cadastro preenchido — {r.Fonte}";
                LblDocStatus.Foreground = Brushes.SeaGreen;
            }
            finally
            {
                _buscandoDocumento = false;
            }
        }

        private void AtualizarStatusDocumento(bool quiet)
        {
            string digits = DocumentValidator.OnlyDigits(TxtCpfCnpj.Text);
            string? tipo = DocumentValidator.DetectTipoPessoa(digits);

            // Com documento, o tipo sai dele. Sem documento, quem escolhe é o usuário.
            CbTipoPessoa.IsEnabled = digits.Length == 0;
            CbTipoPessoa.ToolTip = digits.Length == 0
                ? "Sem CPF/CNPJ: escolha se é pessoa física ou jurídica"
                : "Definido automaticamente pelo CPF/CNPJ";

            if (tipo == "F")
            {
                CbTipoPessoa.SelectedIndex = 0;
                if (digits.Length == 11)
                {
                    bool ok = DocumentValidator.IsValidCpf(digits);
                    LblDocStatus.Text = ok ? "CPF válido · Pessoa Física" : "CPF inválido";
                    LblDocStatus.Foreground = ok ? Brushes.SeaGreen : Brushes.IndianRed;
                }
                else
                {
                    LblDocStatus.Text = $"CPF · {digits.Length}/11 dígitos";
                    LblDocStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
                }
            }
            else if (tipo == "J")
            {
                CbTipoPessoa.SelectedIndex = 1;
                if (digits.Length == 14)
                {
                    bool ok = DocumentValidator.IsValidCnpj(digits);
                    LblDocStatus.Text = ok ? "CNPJ válido · Pessoa Jurídica" : "CNPJ inválido";
                    LblDocStatus.Foreground = ok ? Brushes.SeaGreen : Brushes.IndianRed;
                }
                else
                {
                    LblDocStatus.Text = $"CNPJ · {digits.Length}/14 dígitos";
                    LblDocStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
                }
            }
            else if (digits.Length == 0)
            {
                LblDocStatus.Text = quiet ? "" : "Opcional — obrigatório só para emitir NF-e/NFS-e";
                LblDocStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
            }
            else
            {
                LblDocStatus.Text = $"{digits.Length} dígitos — CPF=11, CNPJ=14";
                LblDocStatus.Foreground = Brushes.DarkOrange;
            }
        }

        private async void TxtCep_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _buscandoCep) return;
            e.Handled = true;
            _buscandoCep = true;
            try
            {
                var result = await CepService.BuscarAsync(TxtCep.Text);
                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage ?? "CEP não encontrado.", "CEP",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TxtCep.Text = result.Cep;
                if (!string.IsNullOrWhiteSpace(result.Logradouro)) TxtLogradouro.Text = result.Logradouro;
                if (!string.IsNullOrWhiteSpace(result.Complemento)) TxtComplemento.Text = result.Complemento;
                if (!string.IsNullOrWhiteSpace(result.Bairro)) TxtBairro.Text = result.Bairro;
                if (!string.IsNullOrWhiteSpace(result.Municipio)) TxtMunicipio.Text = result.Municipio;
                if (!string.IsNullOrWhiteSpace(result.Uf)) TxtUf.Text = result.Uf;
                if (!string.IsNullOrWhiteSpace(result.CodigoIbge)) TxtCodigoIbge.Text = result.CodigoIbge;
                TxtNumero.Focus();
            }
            finally
            {
                _buscandoCep = false;
            }
        }

        private void LimparForm()
        {
            _editingId = null;
            CbTipoPessoa.SelectedIndex = 0;
            TxtNome.Text = TxtRazaoSocial.Text = TxtNomeFantasia.Text = "";
            TxtCpfCnpj.Text = TxtIe.Text = TxtIm.Text = "";
            LblDocStatus.Text = "";
            CbIndicadorIe.SelectedIndex = 2;
            TxtContato.Text = TxtEmail.Text = "";
            TxtCep.Text = TxtLogradouro.Text = TxtNumero.Text = TxtComplemento.Text = "";
            TxtBairro.Text = TxtMunicipio.Text = TxtUf.Text = TxtCodigoIbge.Text = "";
            TxtPais.Text = "Brasil";
            TxtCodigoPais.Text = "1058";
            BtnSalvar.Content = "💾 SALVAR";
        }

        private void LoadGrid()
        {
            var list = new List<ClienteModel>();
            // Digits-only (PostgreSQL)
            const string DigitsExpr = "regexp_replace(COALESCE(cpf_cnpj,''), '[^0-9]', '', 'g')";

            string where = "WHERE 1=1";
            if (!string.IsNullOrEmpty(_filtro))
                // ILIKE: no PostgreSQL o LIKE é sensível a maiúsculas (no SQLite legado não era)
                where += " AND (Nome ILIKE @q OR Cpf_Cnpj ILIKE @q OR RazaoSocial ILIKE @q OR Municipio ILIKE @q OR Contato ILIKE @q)";

            string? tipoTag = (CbFiltroTipo?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (tipoTag == "F")
                where += $" AND (length({DigitsExpr}) = 11 OR (length({DigitsExpr}) NOT IN (11,14) AND COALESCE(TipoPessoa,'F') = 'F'))";
            else if (tipoTag == "J")
                where += $" AND (length({DigitsExpr}) = 14 OR (length({DigitsExpr}) NOT IN (11,14) AND TipoPessoa = 'J'))";

            string? statusTag = (CbFiltroStatus?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (!string.IsNullOrEmpty(statusTag))
                where += " AND IFNULL(Ativo,1) = @at";

            try
            {
                using var conn = Database.GetConnection();
                int total;

                using (var cmdCount = Database.Cmd(conn, $"SELECT COUNT(*) FROM Clientes {where}"))
                {
                    if (!string.IsNullOrEmpty(_filtro)) cmdCount.Parameters.AddWithValue("@q", $"%{_filtro}%");
                    if (!string.IsNullOrEmpty(statusTag)) cmdCount.Parameters.AddWithValue("@at", int.Parse(statusTag));
                    total = Convert.ToInt32(cmdCount.ExecuteScalar() ?? 0);
                    _totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
                    if (_page > _totalPages) _page = _totalPages;
                }

                int offset = (_page - 1) * PageSize;
                using var cmd = Database.Cmd(conn, 
                    $"SELECT * FROM Clientes {where} ORDER BY Nome COLLATE NOCASE LIMIT {PageSize} OFFSET {offset}");
                if (!string.IsNullOrEmpty(_filtro)) cmd.Parameters.AddWithValue("@q", $"%{_filtro}%");
                if (!string.IsNullOrEmpty(statusTag)) cmd.Parameters.AddWithValue("@at", int.Parse(statusTag));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string doc = Str(r, "Cpf_Cnpj");
                    string tipo = DocumentValidator.DetectTipoPessoa(doc) ?? Str(r, "TipoPessoa", "F");

                    list.Add(new ClienteModel
                    {
                        Id = Convert.ToInt64(Database.FieldOrDbNull(r, "Id")),
                        TipoPessoa = tipo,
                        Nome = Str(r, "Nome"),
                        RazaoSocial = Str(r, "RazaoSocial"),
                        NomeFantasia = Str(r, "NomeFantasia"),
                        Contato = Str(r, "Contato"),
                        Email = Str(r, "Email"),
                        CpfCnpj = doc,
                        Ie = Str(r, "Ie"),
                        Im = Str(r, "Im"),
                        IndicadorIe = Str(r, "IndicadorIe", "9"),
                        Cep = Str(r, "Cep"),
                        Logradouro = Str(r, "Logradouro"),
                        Numero = Str(r, "Numero"),
                        Complemento = Str(r, "Complemento"),
                        Bairro = Str(r, "Bairro"),
                        Municipio = Str(r, "Municipio"),
                        Uf = Str(r, "Uf"),
                        CodigoIbge = Str(r, "CodigoIbge"),
                        Pais = Str(r, "Pais", "Brasil"),
                        CodigoPais = Str(r, "CodigoPais", "1058"),
                        Ativo = Database.FieldOrDbNull(r, "Ativo") != DBNull.Value ? Convert.ToInt32(Database.FieldOrDbNull(r, "Ativo")) : 1
                    });
                }

                GridClientes.ItemsSource = list;
                LblPageInfo.Text = $"Pág {_page}/{_totalPages} · {total} cliente(s)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar: {ex.Message}");
            }
        }

        private static object Db(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : s.Trim();

        private static string Str(NpgsqlDataReader r, string col, string def = "")
        {
            try
            {
                var v = r[col];
                return v == DBNull.Value || v == null ? def : v.ToString() ?? def;
            }
            catch { return def; }
        }
    }
}
