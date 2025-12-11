using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace FTO_App
{
    public partial class MainWindow : Window
    {
        private const string localVersion = "1.0";
        private long? _editingId = null;
        
        // Nova variável para controlar a edição de clientes
        private long? _editingClientId = null;

        private string _loggedUser = string.Empty;
        private int _currentPage = 1;
        private int _totalPages = 1;
        private const int ITEMS_PER_PAGE = 30;
        private string _currentFilter = "";
        private bool _isDarkTheme = false;

        public MainWindow()
        {
            InitializeComponent();
            this.Title = ($"FTO - Painel de Acesso {localVersion}");

            try 
            { 
                Database.InitTables(); 
            } 
            catch (Exception ex) 
            { 
                MessageBox.Show($"Erro crítico ao criar banco de dados: {ex.Message}", "Erro Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            if (DpData != null)
                DpData.SelectedDate = DateTime.Today;

            PopulateDateFilters();
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

        private void BtnAutoFit_Click(object sender, RoutedEventArgs e)
        {
            foreach (var column in GridVendas.Columns)
            {
                column.Width = new DataGridLength(1.0, DataGridLengthUnitType.Auto);
                column.Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
                column.Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToHeader);
                column.Width = DataGridLength.Auto;
            }
        }

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme;
            if (_isDarkTheme)
            {
                SetColor("WindowBackgroundBrush", "#1E1E1E");
                SetColor("CardBackgroundBrush", "#2D2D30");
                SetColor("TextBrush", "#FFFFFF");
                SetColor("SecondaryTextBrush", "#BBBBBB");
                SetColor("TitleBrush", "#FFFFFF");
                SetColor("BorderBrush", "#444444");
                SetColor("InputBackgroundBrush", "#3E3E42");
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
                SetColor("WindowBackgroundBrush", "#F5F5F7");
                SetColor("CardBackgroundBrush", "#FFFFFF");
                SetColor("TextBrush", "#000000");
                SetColor("SecondaryTextBrush", "#555555");
                SetColor("TitleBrush", "#0b3d91");
                SetColor("BorderBrush", "#CCCCCC");
                SetColor("InputBackgroundBrush", "#FFFFFF");
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
            this.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private void ShowMsg(string msg) => MessageBox.Show(msg, "FTO", MessageBoxButton.OK, MessageBoxImage.Information);
        private void ShowError(string msg) => MessageBox.Show(msg, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        
        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            string? appPath = Environment.ProcessPath;
            if (appPath != null)
            {
                Process.Start(appPath);
                Application.Current.Shutdown();
            }
        }

        private object GetDbValue(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return DBNull.Value;
            return text.Trim();
        }

        private DateTime ParseDbDate(object value)
        {
            if (value == null || value == DBNull.Value) return DateTime.Today;
            string dateStr = value.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(dateStr)) return DateTime.Today;

            if (DateTime.TryParse(dateStr, new CultureInfo("pt-BR"), DateTimeStyles.None, out DateTime dtResult)) return dtResult;
            if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out dtResult)) return dtResult;

            string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy/MM/dd" };
            if (DateTime.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dtResult)) return dtResult;

            return DateTime.Today;
        }

        private decimal ParseMoney(object? value)
        {
            if (value == null || value == DBNull.Value) return 0;
            string text = value.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(text)) return 0;

            try
            {
                string clean = text.Replace("R$", "").Trim();
                if (clean.Contains(","))
                {
                    clean = clean.Replace(".", ""); 
                    return decimal.Parse(clean, new CultureInfo("pt-BR"));
                }
                return decimal.Parse(clean.Replace(".", ""), CultureInfo.InvariantCulture);
            }
            catch { return 0; }
        }

        private decimal ParseUiMoney(string? text) => ParseMoney(text);

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string u = TxtLoginUser.Text;
            string p = TxtLoginPass.Password;

            if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p)) { ShowError("Preencha os campos."); return; }

            try
            {
                using (var conn = Database.GetConnection())
                using (var cmd = new SQLiteCommand("SELECT Id FROM Users WHERE User = @u AND Senha = @p", conn))
                {
                    cmd.Parameters.AddWithValue("@u", u);
                    cmd.Parameters.AddWithValue("@p", p);
                    if (cmd.ExecuteScalar() != null)
                    {
                        _loggedUser = u;
                        LblWelcome.Text = $"Bem-vindo, {u}!";
                        LoginGrid.Visibility = Visibility.Collapsed;
                        SelectionGrid.Visibility = Visibility.Visible;
                        this.Title = $"FTO - Seleção de Módulo {localVersion}";
                    }
                    else ShowError("Dados incorretos.");
                }
            }
            catch (Exception ex) { ShowError($"Erro login: {ex.Message}"); }
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
                ShowMsg("Usuário criado!");
                BtnVoltarLogin_Click(null, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ERRO REAL: {ex.Message}\n\nDetalhes: {ex.StackTrace}");
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
            SelectionGrid.Visibility = Visibility.Collapsed;
            DashboardGrid.Visibility = Visibility.Visible;
            this.Title = $"FTO - Dashboard de Vendas {localVersion}";
            LoadClients();
            LoadData();
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            _loggedUser = "";
            TxtLoginPass.Password = "";
            SelectionGrid.Visibility = Visibility.Collapsed;
            LoginGrid.Visibility = Visibility.Visible;
            this.Title = $"FTO - Painel de Acesso {localVersion}";
        }

        private void BtnBackToSelect_Click(object sender, RoutedEventArgs e)
        {
            DashboardGrid.Visibility = Visibility.Collapsed;
            SelectionGrid.Visibility = Visibility.Visible;
            this.Title = $"FTO - Seleção de Módulo {localVersion}";
        }

        private void LoadClients()
        {
            CbCliente.Items.Clear();
            try
            {
                using (var conn = Database.GetConnection())
                using (var cmd = new SQLiteCommand("SELECT Nome FROM Clientes ORDER BY Nome ASC", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string nome = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        CbCliente.Items.Add(nome);
                    }
                }
            }
            catch { }
        }

        private void CbCliente_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbCliente.SelectedItem == null) return;
            try
            {
                using (var conn = Database.GetConnection())
                using (var cmd = new SQLiteCommand("SELECT Contato, Cpf_Cnpj FROM Clientes WHERE Nome = @n", conn))
                {
                    cmd.Parameters.AddWithValue("@n", CbCliente.SelectedItem.ToString() ?? "");
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            TxtContato.Text = r["Contato"]?.ToString() ?? "";
                            TxtCpf.Text = r["Cpf_Cnpj"]?.ToString() ?? "";
                        }
                    }
                }
            }
            catch { }
        }

        private void Calc_Lucro(object sender, TextChangedEventArgs e)
        {
            decimal v = ParseUiMoney(TxtVenda.Text);
            decimal g = ParseUiMoney(TxtGastos.Text);
            TxtLucro.Text = (v - g).ToString("C2");
        }

        private void BtnSalvarVenda_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CbCliente.Text) || string.IsNullOrEmpty(TxtVenda.Text))
            {
                ShowError("Cliente e Venda obrigatórios.");
                return;
            }

            string cliNome = CbCliente.Text.Trim();
            
            bool clientExists = false;
            try
            {
                using(var conn = Database.GetConnection())
                {
                    var cmdCheck = new SQLiteCommand("SELECT Count(*) FROM Clientes WHERE Nome = @n", conn);
                    cmdCheck.Parameters.AddWithValue("@n", cliNome);
                    long count = (long)cmdCheck.ExecuteScalar();
                    clientExists = count > 0;
                }
            }
            catch { }

            if (!clientExists)
            {
                if (MessageBox.Show($"O cliente '{cliNome}' não existe. Cadastrar agora?", "Novo Cliente", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    try
                    {
                        Database.ExecuteNonQuery("INSERT INTO Clientes (Nome, Contato, Cpf_Cnpj) VALUES (@n, @c, @d)",
                            new Dictionary<string, object> {
                                {"@n", cliNome},
                                {"@c", GetDbValue(TxtContato.Text)},
                                {"@d", GetDbValue(TxtCpf.Text)}
                            });
                        LoadClients();
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Erro ao criar cliente: {ex.Message}");
                        return;
                    }
                }
            }

            decimal gas = ParseUiMoney(TxtGastos.Text);
            decimal ven = ParseUiMoney(TxtVenda.Text);
            DateTime dt = DpData.SelectedDate ?? DateTime.Today;
            string dateStr = dt.ToString("yyyy-MM-dd HH:mm:ss");

            string sql;
            var p = new Dictionary<string, object> {
                {"@cl", cliNome},
                {"@co", GetDbValue(TxtContato.Text)},
                {"@dt", dateStr},
                {"@ga", gas},
                {"@ve", ven},
                {"@sv", GetDbValue(TxtServico.Text)},
                {"@fp", GetDbValue(CbFormaPag.Text)},
                {"@pg", GetDbValue(CbStatus.Text)},
                {"@cp", GetDbValue(TxtCpf.Text)}
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
                ShowMsg("Salvo com sucesso!");
                ClearForm();
                LoadData();
            }
            catch (Exception ex) { ShowError($"Erro ao salvar venda: {ex.Message}"); }
        }

        private void LoadData()
        {
            var list = new List<Venda>();
            int offset = (_currentPage - 1) * ITEMS_PER_PAGE;
            int total = 0;
            decimal totV = 0, totG = 0;

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
            if (!string.IsNullOrEmpty(_currentFilter))
                where += " AND (Cliente LIKE @q OR Contato LIKE @q OR CPF_CNPJ LIKE @q)";

            try
            {
                using (var conn = Database.GetConnection())
                {
                    var cmdCount = new SQLiteCommand($"SELECT COUNT(*) FROM Vendas {where}", conn);
                    if (!string.IsNullOrEmpty(_currentFilter)) cmdCount.Parameters.AddWithValue("@q", $"%{_currentFilter}%");
                    
                    object? scalar = cmdCount.ExecuteScalar();
                    total = scalar != null && scalar != DBNull.Value ? Convert.ToInt32(scalar) : 0;

                    var cmd = new SQLiteCommand($"SELECT * FROM Vendas {where} ORDER BY Data DESC, Id DESC LIMIT {ITEMS_PER_PAGE} OFFSET {offset}", conn);
                    if (!string.IsNullOrEmpty(_currentFilter)) cmd.Parameters.AddWithValue("@q", $"%{_currentFilter}%");
                    
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            list.Add(new Venda {
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
                    
                    string sumSql = $"SELECT SUM(Venda), SUM(Gastos) FROM Vendas {where}";
                    var cmdSum = new SQLiteCommand(sumSql, conn);
                    if (!string.IsNullOrEmpty(_currentFilter)) cmdSum.Parameters.AddWithValue("@q", $"%{_currentFilter}%");
                    
                    using(var r = cmdSum.ExecuteReader())
                    {
                         if(r.Read())
                         {
                             totV = r.IsDBNull(0) ? 0 : r.GetDecimal(0);
                             totG = r.IsDBNull(1) ? 0 : r.GetDecimal(1);
                         }
                    }
                }

                GridVendas.ItemsSource = list;
                _totalPages = (int)Math.Ceiling((double)total / ITEMS_PER_PAGE);
                if (_totalPages < 1) _totalPages = 1;
                LblPageInfo.Text = $"Pág {_currentPage}/{_totalPages}";
                
                LblTotalRegistros.Text = total.ToString();
                LblTotalVendas.Text = totV.ToString("C2");
                LblTotalGastos.Text = totG.ToString("C2");
                LblTotalLucros.Text = (totV - totG).ToString("C2");
            }
            catch (Exception ex)
            {
                ShowError($"Erro ao carregar dados: {ex.Message}");
            }
        }

        private void BtnEditRow_Click(object sender, RoutedEventArgs e)
        {
            if (GridVendas.SelectedItem is Venda v)
            {
                _editingId = v.Id;
                CbCliente.Text = v.Cliente ?? "";
                TxtContato.Text = v.Contato ?? "";
                DpData.SelectedDate = v.Data;
                TxtCpf.Text = v.CPF_CNPJ ?? "";
                TxtServico.Text = v.TipoServico ?? "";
                CbFormaPag.Text = v.FormaPag ?? "";
                CbStatus.Text = v.Pago ?? "";
                TxtGastos.Text = v.Gastos.ToString("N2");
                TxtVenda.Text = v.VendaValor.ToString("N2");

                BtnSalvar.Content = "ATUALIZAR VENDA";
                BtnSalvar.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#FFD700");
                BtnSalvar.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#0b3d91");
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (GridVendas.SelectedItem is Venda v)
            {
                if (MessageBox.Show("Excluir venda?", "Confirma", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    try
                    {
                        Database.ExecuteNonQuery("DELETE FROM Vendas WHERE Id=@id", new Dictionary<string, object>{{"@id", v.Id}});
                        LoadData();
                    }
                    catch (Exception ex) { ShowError(ex.Message); }
                }
            }
        }

        private void BtnMarkPaid_Click(object sender, RoutedEventArgs e)
        {
            if (GridVendas.SelectedItem is Venda v)
            {
                try
                {
                    Database.ExecuteNonQuery("UPDATE Vendas SET Pago='Pago' WHERE Id=@id", new Dictionary<string, object>{{"@id", v.Id}});
                    LoadData();
                }
                catch (Exception ex) { ShowError(ex.Message); }
            }
        }

        private void BtnLimpar_Click(object sender, RoutedEventArgs e) => ClearForm();

        private void ClearForm()
        {
            _editingId = null;
            BtnSalvar.Content = "SALVAR VENDA";
            BtnSalvar.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#0b3d91");
            BtnSalvar.Foreground = Brushes.White;

            CbCliente.Text = ""; TxtContato.Text = ""; TxtCpf.Text = "";
            TxtServico.Text = ""; TxtGastos.Text = ""; TxtVenda.Text = ""; TxtLucro.Text = "";
            DpData.SelectedDate = DateTime.Today;
        }

        private void TxtBusca_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentFilter = TxtBusca.Text.Trim();
            _currentPage = 1;
            LoadData();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _currentFilter = TxtBusca.Text.Trim();
            _currentPage = 1;
            LoadData();
        }

        private void BtnPrevPage_Click(object sender, RoutedEventArgs e) { if(_currentPage > 1) { _currentPage--; LoadData(); } }
        private void BtnNextPage_Click(object sender, RoutedEventArgs e) { if(_currentPage < _totalPages) { _currentPage++; LoadData(); } }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            string fileName = "Vendas_Total";
            bool mesSelecionado = CbFiltroMes != null && CbFiltroMes.SelectedIndex > 0;
            bool anoSelecionado = CbFiltroAno != null && CbFiltroAno.SelectedIndex > 0;

            if (mesSelecionado && anoSelecionado)
            {
                string mes = CbFiltroMes.SelectedItem.ToString() ?? "";
                string ano = CbFiltroAno.SelectedItem.ToString() ?? "";
                fileName = $"Vendas_{mes}_{ano}";
            }
            else if (mesSelecionado) fileName = $"Vendas_{CbFiltroMes.SelectedItem}";
            else if (anoSelecionado) fileName = $"Vendas_{CbFiltroAno.SelectedItem}";

            SaveFileDialog sfd = new SaveFileDialog { Filter = "Excel|*.xlsx", FileName = $"{fileName}.xlsx" };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    using (var wb = new XLWorkbook())
                    {
                        var ws = wb.Worksheets.Add("Vendas");
                        ws.Cell(1, 1).Value = "ID";
                        ws.Cell(1, 2).Value = "Cliente";
                        ws.Cell(1, 3).Value = "Contato";
                        ws.Cell(1, 4).Value = "Data";
                        ws.Cell(1, 5).Value = "Gastos";       
                        
                        ws.Cell(1, 6).Value = "Venda";        
                        ws.Cell(1, 7).Value = "Lucros";       
                        ws.Cell(1, 8).Value = "Tipo Serviço";
                        ws.Cell(1, 9).Value = "Forma Pag.";
                        ws.Cell(1, 10).Value = "Pago ou não";
                        ws.Cell(1, 11).Value = "CPF/CNPJ";
                        
                        var headerRange = ws.Range(1, 1, 1, 11);
                        headerRange.Style.Font.Bold = true;
                        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0b3d91");
                        headerRange.Style.Font.FontColor = XLColor.White;

                        List<Venda>? list = GridVendas.ItemsSource as List<Venda>;
                        if (list != null)
                        {
                            for (int i = 0; i < list.Count; i++)
                            {
                                int row = i + 2;
                                var item = list[i];

                                ws.Cell(row, 1).Value = item.Id;
                                ws.Cell(row, 2).Value = item.Cliente;
                                ws.Cell(row, 3).Value = item.Contato;
                                ws.Cell(row, 4).Value = item.Data;
                                
                                ws.Cell(row, 5).Value = item.Gastos;
                                ws.Cell(row, 5).Style.NumberFormat.Format = "R$ #,##0.00";
                                
                                ws.Cell(row, 6).Value = item.VendaValor;
                                ws.Cell(row, 6).Style.NumberFormat.Format = "R$ #,##0.00";
                                
                                ws.Cell(row, 7).Value = item.Lucros;
                                ws.Cell(row, 7).Style.NumberFormat.Format = "R$ #,##0.00";

                                ws.Cell(row, 8).Value = item.TipoServico;
                                ws.Cell(row, 9).Value = item.FormaPag;
                                ws.Cell(row, 10).Value = item.Pago;
                                ws.Cell(row, 11).Value = item.CPF_CNPJ;
                            }
                        }
                        
                        ws.Columns().AdjustToContents();
                        wb.SaveAs(sfd.FileName);
                        ShowMsg("Exportado com sucesso!");
                    }
                }
                catch (Exception ex) 
                { 
                    ShowError($"Erro ao exportar: {ex.Message}"); 
                }
            }
        }

        public class ClienteModel {
            public long Id { get; set; }
            public string Nome { get; set; } = string.Empty;
            public string Contato { get; set; } = string.Empty;
            public string CpfCnpj { get; set; } = string.Empty;
        }

        private void BtnClientManager_Click(object sender, RoutedEventArgs e)
        {
            ClientsGrid.Visibility = Visibility.Visible;
            LoadClientsGrid();
        }

        private void BtnCloseClients_Click(object sender, RoutedEventArgs e)
        {
            // Alterado: Limpa o formulário ao fechar para evitar inconsistências
            ClearClientForm();
            ClientsGrid.Visibility = Visibility.Collapsed;
            LoadClients();
        }

        private void LoadClientsGrid()
        {
            var list = new List<ClienteModel>();
            try
            {
                using (var conn = Database.GetConnection())
                using (var cmd = new SQLiteCommand("SELECT * FROM Clientes ORDER BY Nome", conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new ClienteModel {
                            Id = r.GetInt64(0),
                            Nome = r.IsDBNull(1) ? "" : r.GetString(1),
                            Contato = r["Contato"]?.ToString() ?? "",
                            CpfCnpj = r["Cpf_Cnpj"]?.ToString() ?? ""
                        });
                    }
                }
                GridClientes.ItemsSource = list;
            }
            catch (Exception ex) { ShowError(ex.Message); }
        }

        // Método Substituído: Agora lida com Inserção e Atualização
        private void BtnAddClientInternal_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrWhiteSpace(TxtNewCliNome.Text)) return;

            try
            {
                if (_editingClientId.HasValue)
                {
                    // Update
                     Database.ExecuteNonQuery("UPDATE Clientes SET Nome=@n, Contato=@c, Cpf_Cnpj=@d WHERE Id=@id",
                        new Dictionary<string, object> {
                            {"@n", TxtNewCliNome.Text},
                            {"@c", GetDbValue(TxtNewCliContato.Text)},
                            {"@d", GetDbValue(TxtNewCliDoc.Text)},
                            {"@id", _editingClientId.Value}
                        });
                    ShowMsg("Cliente atualizado com sucesso!");
                }
                else
                {
                    // Insert
                    Database.ExecuteNonQuery("INSERT INTO Clientes (Nome, Contato, Cpf_Cnpj) VALUES (@n, @c, @d)",
                        new Dictionary<string, object> {
                            {"@n", TxtNewCliNome.Text},
                            {"@c", GetDbValue(TxtNewCliContato.Text)},
                            {"@d", GetDbValue(TxtNewCliDoc.Text)}
                        });
                    ShowMsg("Cliente cadastrado!");
                }

                ClearClientForm(); // Limpa form e reseta botão
                LoadClientsGrid();
            }
            catch (Exception ex) { ShowError($"Erro ao salvar: {ex.Message}"); }
        }

        // Novo Método: Botão Editar da Grid de Clientes
        private void BtnEditClientInternal_Click(object sender, RoutedEventArgs e)
        {
            if (GridClientes.SelectedItem is ClienteModel c)
            {
                _editingClientId = c.Id;
                TxtNewCliNome.Text = c.Nome;
                TxtNewCliContato.Text = c.Contato;
                TxtNewCliDoc.Text = c.CpfCnpj;

                // Muda o botão de "Adicionar" para "Salvar Alteração"
                // Requer que o botão tenha x:Name="BtnSaveClient" no XAML
                if (BtnSaveClient != null)
                {
                    BtnSaveClient.Content = "💾 Salvar Alteração";
                    BtnSaveClient.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#FFD700");
                    BtnSaveClient.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#0b3d91");
                }
            }
            else
            {
                ShowMsg("Selecione um cliente na lista para editar.");
            }
        }

        private void BtnDeleteClientInternal_Click(object sender, RoutedEventArgs e)
        {
            if (GridClientes.SelectedItem is ClienteModel c)
            {
                if(MessageBox.Show($"Excluir {c.Nome}?", "Confirmar", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                    try
                    {
                        Database.ExecuteNonQuery("DELETE FROM Clientes WHERE Id=@id", new Dictionary<string, object>{{"@id", c.Id}});
                        LoadClientsGrid();
                        // Se excluiu o que estava editando, limpa
                        if (_editingClientId == c.Id) ClearClientForm();
                    }
                    catch (Exception ex) { ShowError(ex.Message); }
                }
            }
        }

        // Novo Método: Reseta o formulário de clientes
        private void ClearClientForm()
        {
            _editingClientId = null;
            TxtNewCliNome.Text = ""; 
            TxtNewCliContato.Text = ""; 
            TxtNewCliDoc.Text = "";

            if (BtnSaveClient != null)
            {
                BtnSaveClient.Content = "+ Adicionar";
                BtnSaveClient.ClearValue(Button.BackgroundProperty);
                BtnSaveClient.ClearValue(Button.ForegroundProperty);
            }
        }
        
        private void BtnQuickAddClient_Click(object sender, RoutedEventArgs e)
        {
             string nome = CbCliente.Text;
             if(string.IsNullOrWhiteSpace(nome)) return;
             
             bool exists = false;
             try {
                 using(var conn = Database.GetConnection()) {
                     var cmd = new SQLiteCommand("SELECT Count(*) FROM Clientes WHERE Nome = @n", conn);
                     cmd.Parameters.AddWithValue("@n", nome);
                     exists = (long)cmd.ExecuteScalar() > 0;
                 }
             } catch {}
             
             if(!exists) {
                try
                {
                    Database.ExecuteNonQuery("INSERT INTO Clientes (Nome, Contato, Cpf_Cnpj) VALUES (@n, @c, @d)",
                        new Dictionary<string, object> {
                            {"@n", nome},
                            {"@c", GetDbValue(TxtContato.Text)},
                            {"@d", GetDbValue(TxtCpf.Text)}
                        });
                    LoadClients();
                    ShowMsg("Cliente cadastrado!");
                }
                catch (Exception ex) { ShowError(ex.Message); }
             }
        }
    }
}