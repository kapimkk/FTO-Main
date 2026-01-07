using ClosedXML.Excel;
using FTO_App.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FTO_App.Views
{
    public partial class DashboardView : UserControl
    {
        public event EventHandler OnLogoutRequest;

        private long? _editingId = null;
        private long? _editingClientId = null;
        private int _currentPage = 1;
        private int _totalPages = 1;
        private const int ITEMS_PER_PAGE = 30;
        private string _currentFilter = "";
        private bool _isDarkTheme = false;

        public DashboardView()
        {
            InitializeComponent();
            if (DpData != null) DpData.SelectedDate = DateTime.Today;
            PopulateDateFilters();
            LoadClients();
            LoadData();
        }

        private void BtnBackToSelect_Click(object sender, RoutedEventArgs e)
        {
            OnLogoutRequest?.Invoke(this, EventArgs.Empty);
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            string? appPath = Environment.ProcessPath;
            if (appPath != null)
            {
                Process.Start(appPath);
                Application.Current.Shutdown();
            }
        }

        private void PopulateDateFilters()
        {
            if (CbFiltroMes == null || CbFiltroAno == null) return;
            CbFiltroMes.Items.Clear();
            CbFiltroMes.Items.Add("Mês (Todos)");
            string[] meses = { "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro" };
            foreach (var m in meses) CbFiltroMes.Items.Add(m);
            CbFiltroMes.SelectedIndex = 0;

            CbFiltroAno.Items.Clear();
            CbFiltroAno.Items.Add("Ano (Todos)");
            for (int y = 2025; y <= 2060; y++) CbFiltroAno.Items.Add(y.ToString());
            CbFiltroAno.SelectedIndex = 0;

            CbFiltroMes.SelectionChanged += (s, e) => { _currentPage = 1; LoadData(); };
            CbFiltroAno.SelectionChanged += (s, e) => { _currentPage = 1; LoadData(); };
        }

        // --- THEME TOGGLE (ATUALIZADO) ---
        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme;
            if (_isDarkTheme)
            {
                // MODO ESCURO
                SetColor("WindowBackgroundBrush", "#1E1E1E");
                SetColor("CardBackgroundBrush", "#2D2D30");
                SetColor("TextBrush", "#FFFFFF");
                SetColor("SecondaryTextBrush", "#BBBBBB");
                SetColor("TitleBrush", "#FFFFFF");
                SetColor("BorderBrush", "#444444");

                // Inputs Escuros
                SetColor("InputBackgroundBrush", "#3E3E42");
                SetColor("InputTextBrush", "#FFFFFF");

                SetColor("ProfitBackgroundBrush", "#1B5E20");
                SetColor("GridHeaderBrush", "#252526");
                SetColor("GridHeaderForeground", "#DDDDDD");
                SetColor("RowBackgroundBrush", "#2D2D30");
                SetColor("GridAltRowBrush", "#333337");
                SetColor("GridLinesBrush", "#404040");
                SetColor("PositiveBrush", "#81C784");
                SetColor("NegativeBrush", "#E57373");
                SetColor("ActionPayBg", "#1B5E20"); SetColor("ActionPayFg", "#A5D6A7");
                SetColor("ActionEditBg", "#0D47A1"); SetColor("ActionEditFg", "#90CAF9");
                SetColor("ActionDelBg", "#B71C1C"); SetColor("ActionDelFg", "#EF9A9A");
                BtnTheme.Content = "☀️ Claro";
            }
            else
            {
                // MODO CLARO
                SetColor("WindowBackgroundBrush", "#F5F5F7");
                SetColor("CardBackgroundBrush", "#FFFFFF");
                SetColor("TextBrush", "#000000");
                SetColor("SecondaryTextBrush", "#555555");
                SetColor("TitleBrush", "#0b3d91");
                SetColor("BorderBrush", "#CCCCCC");

                // Inputs Claros
                SetColor("InputBackgroundBrush", "#FFFFFF");
                SetColor("InputTextBrush", "#000000");

                SetColor("ProfitBackgroundBrush", "#F1F8E9");
                SetColor("GridHeaderBrush", "#E0E0E0");
                SetColor("GridHeaderForeground", "#000000");
                SetColor("RowBackgroundBrush", "#FFFFFF");
                SetColor("GridAltRowBrush", "#FAFAFA");
                SetColor("GridLinesBrush", "#D0D0D0");
                SetColor("PositiveBrush", "#2E7D32");
                SetColor("NegativeBrush", "#C62828");
                SetColor("ActionPayBg", "#E8F5E9"); SetColor("ActionPayFg", "#2E7D32");
                SetColor("ActionEditBg", "#E3F2FD"); SetColor("ActionEditFg", "#1565C0");
                SetColor("ActionDelBg", "#FFEBEE"); SetColor("ActionDelFg", "#C62828");
                BtnTheme.Content = "🌙 Tema";
            }
        }

        private void SetColor(string key, string hex)
        {
            Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private object GetDbValue(string? text) => string.IsNullOrWhiteSpace(text) ? DBNull.Value : text.Trim();

        private decimal ParseMoney(object? value)
        {
            if (value == null || value == DBNull.Value) return 0;
            string text = value.ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text)) return 0;
            try
            {
                string clean = text.Replace("R$", "").Replace(" ", "").Trim();
                if (clean.Contains(".") && !clean.Contains(","))
                    return decimal.Parse(clean, CultureInfo.InvariantCulture);
                return decimal.Parse(clean, NumberStyles.Any, new CultureInfo("pt-BR"));
            }
            catch { return 0; }
        }

        private decimal ParseUiMoney(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            try
            {
                string clean = text.Replace("R$", "").Trim();
                return decimal.Parse(clean, NumberStyles.Currency, new CultureInfo("pt-BR"));
            }
            catch { return 0; }
        }

        private DateTime ParseDbDate(object value)
        {
            if (value == null || value == DBNull.Value) return DateTime.Today;
            string dateStr = value.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(dateStr)) return DateTime.Today;

            if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtIso))
                return dtIso;

            if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtIsoFull))
                return dtIsoFull;

            if (DateTime.TryParse(dateStr, new CultureInfo("pt-BR"), DateTimeStyles.None, out DateTime dtBr))
                return dtBr;

            return DateTime.Today;
        }

        private void LoadData()
        {
            var list = new List<Venda>();
            int offset = (_currentPage - 1) * ITEMS_PER_PAGE;
            int totalRegistros = 0;
            decimal totalVendas = 0;
            decimal totalGastos = 0;

            string whereDate = "";
            if (CbFiltroMes != null && CbFiltroMes.SelectedIndex > 0)
            {
                string monthStr = CbFiltroMes.SelectedIndex.ToString("00");
                whereDate += $" AND (strftime('%m', Data) = '{monthStr}' OR substr(Data, 6, 2) = '{monthStr}' OR substr(Data, 4, 2) = '{monthStr}')";
            }
            if (CbFiltroAno != null && CbFiltroAno.SelectedIndex > 0)
            {
                string yearStr = CbFiltroAno.SelectedItem.ToString() ?? "";
                whereDate += $" AND (strftime('%Y', Data) = '{yearStr}' OR substr(Data, 1, 4) = '{yearStr}' OR substr(Data, 7, 4) = '{yearStr}')";
            }

            string where = " WHERE 1=1 " + whereDate;
            if (!string.IsNullOrEmpty(_currentFilter)) where += " AND (Cliente LIKE @q OR Contato LIKE @q OR CPF_CNPJ LIKE @q)";

            try
            {
                using (var conn = Database.GetConnection())
                {
                    var cmdCount = new SQLiteCommand($"SELECT COUNT(*) FROM Vendas {where}", conn);
                    if (!string.IsNullOrEmpty(_currentFilter)) cmdCount.Parameters.AddWithValue("@q", $"%{_currentFilter}%");
                    object? scalar = cmdCount.ExecuteScalar();
                    totalRegistros = scalar != null ? Convert.ToInt32(scalar) : 0;

                    var cmd = new SQLiteCommand($"SELECT * FROM Vendas {where} ORDER BY Data DESC, Id DESC LIMIT {ITEMS_PER_PAGE} OFFSET {offset}", conn);
                    if (!string.IsNullOrEmpty(_currentFilter)) cmd.Parameters.AddWithValue("@q", $"%{_currentFilter}%");

                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            list.Add(new Venda
                            {
                                Id = r.GetInt64(0),
                                Cliente = r["Cliente"]?.ToString() ?? "",
                                Contato = r["Contato"]?.ToString() ?? "",
                                Data = ParseDbDate(r["Data"]),
                                Gastos = ParseMoney(r["Gastos"]),
                                VendaValor = ParseMoney(r["Venda"]),
                                TipoServico = r["TipoServico"]?.ToString() ?? "",
                                FormaPag = r["FormaPag"]?.ToString() ?? "",
                                Pago = r["Pago"]?.ToString() ?? "",
                                CPF_CNPJ = r["CPF_CNPJ"]?.ToString() ?? ""
                            });
                        }
                    }

                    var cmdSum = new SQLiteCommand($"SELECT Venda, Gastos FROM Vendas {where}", conn);
                    if (!string.IsNullOrEmpty(_currentFilter)) cmdSum.Parameters.AddWithValue("@q", $"%{_currentFilter}%");
                    using (var rSum = cmdSum.ExecuteReader())
                    {
                        while (rSum.Read())
                        {
                            totalVendas += ParseMoney(rSum["Venda"]);
                            totalGastos += ParseMoney(rSum["Gastos"]);
                        }
                    }
                }

                GridVendas.ItemsSource = list;
                _totalPages = (int)Math.Ceiling((double)totalRegistros / ITEMS_PER_PAGE);
                if (_totalPages < 1) _totalPages = 1;
                LblPageInfo.Text = $"Pág {_currentPage}/{_totalPages}";
                LblTotalRegistros.Text = totalRegistros.ToString();
                LblTotalVendas.Text = totalVendas.ToString("C2");
                LblTotalGastos.Text = totalGastos.ToString("C2");
                LblTotalLucros.Text = (totalVendas - totalGastos).ToString("C2");
            }
            catch (Exception ex) { MessageBox.Show($"Erro ao carregar dados: {ex.Message}"); }
        }

        private void BtnSalvarVenda_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CbCliente.Text) || string.IsNullOrEmpty(TxtVenda.Text))
            {
                MessageBox.Show("Cliente e Venda obrigatórios.");
                return;
            }

            string cliNome = CbCliente.Text.Trim();
            bool clientExists = false;
            try
            {
                using (var conn = Database.GetConnection())
                {
                    var cmd = new SQLiteCommand("SELECT Count(*) FROM Clientes WHERE Nome = @n", conn);
                    cmd.Parameters.AddWithValue("@n", cliNome);
                    clientExists = (long)cmd.ExecuteScalar() > 0;
                }
            }
            catch { }

            if (!clientExists && MessageBox.Show($"O cliente '{cliNome}' não existe. Cadastrar agora?", "Novo Cliente", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    Database.ExecuteNonQuery("INSERT INTO Clientes (Nome, Contato, Cpf_Cnpj) VALUES (@n, @c, @d)",
                        new Dictionary<string, object> { { "@n", cliNome }, { "@c", GetDbValue(TxtContato.Text) }, { "@d", GetDbValue(TxtCpf.Text) } });
                    LoadClients();
                }
                catch { }
            }

            // SALVANDO DATA SEM HORA (yyyy-MM-dd)
            string dataFormatada = (DpData.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");

            string sql;
            var p = new Dictionary<string, object> {
                {"@cl", cliNome}, {"@co", GetDbValue(TxtContato.Text)},
                {"@dt", dataFormatada},
                {"@ga", ParseUiMoney(TxtGastos.Text)}, {"@ve", ParseUiMoney(TxtVenda.Text)}, {"@sv", GetDbValue(TxtServico.Text)},
                {"@fp", GetDbValue(CbFormaPag.Text)}, {"@pg", GetDbValue(CbStatus.Text)}, {"@cp", GetDbValue(TxtCpf.Text)}
            };

            if (_editingId.HasValue)
            {
                sql = "UPDATE Vendas SET Cliente=@cl, Contato=@co, Data=@dt, Gastos=@ga, Venda=@ve, TipoServico=@sv, FormaPag=@fp, Pago=@pg, CPF_CNPJ=@cp WHERE Id=@id";
                p.Add("@id", _editingId.Value);
            }
            else
            {
                sql = "INSERT INTO Vendas (Cliente, Contato, Data, Gastos, Venda, TipoServico, FormaPag, Pago, CPF_CNPJ) VALUES (@cl, @co, @dt, @ga, @ve, @sv, @fp, @pg, @cp)";
            }

            try
            {
                Database.ExecuteNonQuery(sql, p);
                MessageBox.Show("Salvo!");
                ClearForm();
                LoadData();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void BtnEditRow_Click(object sender, RoutedEventArgs e)
        {
            if (GridVendas.SelectedItem is Venda v)
            {
                _editingId = v.Id;
                CbCliente.Text = v.Cliente; TxtContato.Text = v.Contato;
                DpData.SelectedDate = v.Data; TxtCpf.Text = v.CPF_CNPJ;
                TxtServico.Text = v.TipoServico; CbFormaPag.Text = v.FormaPag;
                CbStatus.Text = v.Pago;
                TxtGastos.Text = v.Gastos.ToString("N2"); TxtVenda.Text = v.VendaValor.ToString("N2");
                BtnSalvar.Content = "ATUALIZAR VENDA";
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (GridVendas.SelectedItem is Venda v && MessageBox.Show("Excluir?", "Confirma", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Database.ExecuteNonQuery("DELETE FROM Vendas WHERE Id=@id", new Dictionary<string, object> { { "@id", v.Id } });
                LoadData();
            }
        }

        private void BtnMarkPaid_Click(object sender, RoutedEventArgs e)
        {
            if (GridVendas.SelectedItem is Venda v)
            {
                Database.ExecuteNonQuery("UPDATE Vendas SET Pago='Pago' WHERE Id=@id", new Dictionary<string, object> { { "@id", v.Id } });
                LoadData();
            }
        }

        private void BtnLimpar_Click(object sender, RoutedEventArgs e) => ClearForm();
        private void ClearForm()
        {
            _editingId = null;
            BtnSalvar.Content = "SALVAR VENDA";
            CbCliente.Text = ""; TxtContato.Text = ""; TxtCpf.Text = "";
            TxtServico.Text = ""; TxtGastos.Text = ""; TxtVenda.Text = ""; TxtLucro.Text = "";
            DpData.SelectedDate = DateTime.Today;
        }

        private void Calc_Lucro(object sender, TextChangedEventArgs e)
        {
            decimal v = ParseUiMoney(TxtVenda.Text);
            decimal g = ParseUiMoney(TxtGastos.Text);
            TxtLucro.Text = (v - g).ToString("C2");
        }

        private void LoadClients()
        {
            CbCliente.Items.Clear();
            try
            {
                using (var conn = Database.GetConnection())
                using (var cmd = new SQLiteCommand("SELECT Nome FROM Clientes ORDER BY Nome", conn))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) CbCliente.Items.Add(r.GetString(0));
            }
            catch { }
        }

        private void CbCliente_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbCliente.SelectedItem == null) return;
            try
            {
                using (var conn = Database.GetConnection())
                using (var cmd = new SQLiteCommand("SELECT Contato, Cpf_Cnpj FROM Clientes WHERE Nome=@n", conn))
                {
                    cmd.Parameters.AddWithValue("@n", CbCliente.SelectedItem.ToString());
                    using (var r = cmd.ExecuteReader())
                        if (r.Read()) { TxtContato.Text = r["Contato"]?.ToString(); TxtCpf.Text = r["Cpf_Cnpj"]?.ToString(); }
                }
            }
            catch { }
        }

        private void BtnQuickAddClient_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CbCliente.Text)) return;
            try
            {
                Database.ExecuteNonQuery("INSERT INTO Clientes (Nome, Contato, Cpf_Cnpj) VALUES (@n, @c, @d)",
                    new Dictionary<string, object> { { "@n", CbCliente.Text }, { "@c", GetDbValue(TxtContato.Text) }, { "@d", GetDbValue(TxtCpf.Text) } });
                LoadClients();
                MessageBox.Show("Cliente cadastrado!");
            }
            catch { }
        }

        private void BtnClientManager_Click(object sender, RoutedEventArgs e) { ClientsGrid.Visibility = Visibility.Visible; LoadClientsGrid(); }
        private void BtnCloseClients_Click(object sender, RoutedEventArgs e) { ClientsGrid.Visibility = Visibility.Collapsed; LoadClients(); ClearClientForm(); }

        private void LoadClientsGrid()
        {
            var list = new List<ClienteModel>();
            try
            {
                using (var conn = Database.GetConnection())
                using (var cmd = new SQLiteCommand("SELECT * FROM Clientes ORDER BY Nome", conn))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(new ClienteModel { Id = r.GetInt64(0), Nome = r.GetString(1), Contato = r["Contato"]?.ToString() ?? "", CpfCnpj = r["Cpf_Cnpj"]?.ToString() ?? "" });
                GridClientes.ItemsSource = list;
            }
            catch { }
        }

        private void BtnAddClientInternal_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNewCliNome.Text)) return;
            string sql = _editingClientId.HasValue ? "UPDATE Clientes SET Nome=@n, Contato=@c, Cpf_Cnpj=@d WHERE Id=@id" : "INSERT INTO Clientes (Nome, Contato, Cpf_Cnpj) VALUES (@n, @c, @d)";
            var p = new Dictionary<string, object> { { "@n", TxtNewCliNome.Text }, { "@c", GetDbValue(TxtNewCliContato.Text) }, { "@d", GetDbValue(TxtNewCliDoc.Text) } };
            if (_editingClientId.HasValue) p.Add("@id", _editingClientId.Value);

            try
            {
                Database.ExecuteNonQuery(sql, p);
                ClearClientForm(); LoadClientsGrid();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void BtnEditClientInternal_Click(object sender, RoutedEventArgs e)
        {
            if (GridClientes.SelectedItem is ClienteModel c)
            {
                _editingClientId = c.Id;
                TxtNewCliNome.Text = c.Nome; TxtNewCliContato.Text = c.Contato; TxtNewCliDoc.Text = c.CpfCnpj;
                BtnSaveClient.Content = "💾 Salvar";
            }
        }

        private void BtnDeleteClientInternal_Click(object sender, RoutedEventArgs e)
        {
            if (GridClientes.SelectedItem is ClienteModel c && MessageBox.Show("Excluir?", "Confirma", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Database.ExecuteNonQuery("DELETE FROM Clientes WHERE Id=@id", new Dictionary<string, object> { { "@id", c.Id } });
                LoadClientsGrid();
            }
        }

        private void ClearClientForm() { _editingClientId = null; TxtNewCliNome.Text = ""; TxtNewCliContato.Text = ""; TxtNewCliDoc.Text = ""; BtnSaveClient.Content = "+ Adicionar"; }

        private void BtnAutoFit_Click(object sender, RoutedEventArgs e)
        {
            foreach (var column in GridVendas.Columns)
            {
                column.Width = new DataGridLength(1.0, DataGridLengthUnitType.Auto);
                column.Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToHeader);
                column.Width = DataGridLength.Auto;
            }
        }

        private void TxtBusca_TextChanged(object sender, TextChangedEventArgs e) { _currentFilter = TxtBusca.Text.Trim(); _currentPage = 1; LoadData(); }
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) { _currentFilter = TxtBusca.Text.Trim(); _currentPage = 1; LoadData(); }
        private void BtnPrevPage_Click(object sender, RoutedEventArgs e) { if (_currentPage > 1) { _currentPage--; LoadData(); } }
        private void BtnNextPage_Click(object sender, RoutedEventArgs e) { if (_currentPage < _totalPages) { _currentPage++; LoadData(); } }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog { Filter = "Excel|*.xlsx", FileName = "Vendas.xlsx" };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    using (var wb = new XLWorkbook())
                    {
                        var ws = wb.Worksheets.Add("Vendas");
                        var list = GridVendas.ItemsSource as List<Venda>;
                        ws.Cell(1, 1).InsertTable(list);
                        wb.SaveAs(sfd.FileName);
                        MessageBox.Show("Exportado!");
                    }
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            }
        }
    }
}