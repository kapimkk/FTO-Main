using System;
using System.Windows;
using FTO_App.Models;
using FTO_App.Views;

namespace FTO_App.Services
{
    /// <summary>
    /// Impressão de cupom via layout WPF (padrão Imperial Colors).
    /// </summary>
    public static class ThermalPrinterService
    {
        public static void PrintReceipt(Venda venda)
        {
            string printer = DeviceSettingsStore.Current.SelectedPrinter;
            if (string.IsNullOrWhiteSpace(printer))
                throw new InvalidOperationException(
                    "Nenhuma impressora selecionada. Configure na tela de módulos (após o login).");

            var cupom = new ReceiptCupomView();
            cupom.Inicializar(venda);
            cupom.PrepararParaImpressao(ReceiptCupomView.LarguraCupomPx);

            if (!CupomPrintHelper.ImprimirNaImpressoraConfigurada(
                    cupom.CupomParaImpressao,
                    $"Cupom FTO {venda.Id}",
                    printer,
                    out string? erro))
                throw new InvalidOperationException(erro ?? "Falha ao imprimir o cupom.");
        }

        public static bool IsPrinterConfigured =>
            !string.IsNullOrWhiteSpace(DeviceSettingsStore.Current.SelectedPrinter);
    }
}
