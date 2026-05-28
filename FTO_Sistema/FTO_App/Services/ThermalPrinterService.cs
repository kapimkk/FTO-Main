using System;
using FTO_App.Models;

namespace FTO_App.Services
{
    /// <summary>
    /// Impressão de cupom não fiscal em impressoras ESC/POS (ex.: MP-2500 HT).
    /// </summary>
    public static class ThermalPrinterService
    {
        public static void PrintReceipt(Venda venda, string? operador, bool incluirLinhaAssinatura)
        {
            string printer = DeviceSettingsStore.Current.SelectedPrinter;
            if (string.IsNullOrWhiteSpace(printer))
                throw new InvalidOperationException(
                    "Nenhuma impressora selecionada. Configure na tela de módulos (após o login).");

            byte[] data = ReceiptLayout.BuildPrintData(venda, operador, incluirLinhaAssinatura);

            RawPrinterHelper.SendBytes(printer, data);
        }

        public static bool IsPrinterConfigured =>
            !string.IsNullOrWhiteSpace(DeviceSettingsStore.Current.SelectedPrinter);
    }
}
