using System;
using System.Windows;
using FTO_App.Models;
using FTO_App.Services;

namespace FTO_App.Views
{
    public partial class ConfirmPrintWindow : Window
    {
        private readonly Venda _venda;

        public ConfirmPrintWindow(Venda venda)
        {
            InitializeComponent();
            _venda = venda ?? throw new ArgumentNullException(nameof(venda));
            AtualizarPreview();
            ChkAssinatura.Checked += (_, _) => AtualizarPreview();
            ChkAssinatura.Unchecked += (_, _) => AtualizarPreview();
            TxtOperador.TextChanged += (_, _) => AtualizarPreview();
        }

        private void AtualizarPreview()
        {
            TxtPreview.Text = ReceiptLayout.GetPreviewText(
                _venda,
                TxtOperador.Text,
                ChkAssinatura.IsChecked == true);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnConfirmPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ThermalPrinterService.IsPrinterConfigured)
                {
                    MessageBox.Show(
                        "Selecione uma impressora na tela de módulos (após o login).\n" +
                        "Recomendado: MP-2500 HT.",
                        "Impressora não configurada",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                ThermalPrinterService.PrintReceipt(
                    _venda,
                    TxtOperador.Text,
                    ChkAssinatura.IsChecked == true);

                MessageBox.Show(
                    "Cupom enviado para a impressora com sucesso!",
                    "Impressão",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Não foi possível imprimir.\n\n{ex.Message}\n\n" +
                    "Verifique se a impressora MP-2500 HT está ligada, conectada e definida como padrão ou selecionada nos módulos.",
                    "Erro na impressão",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
