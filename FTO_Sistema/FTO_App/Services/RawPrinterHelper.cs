using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FTO_App.Services
{
    /// <summary>
    /// Envio de bytes brutos (ESC/POS) para a fila da impressora no Windows.
    /// </summary>
    internal static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class DOCINFO
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pDocName = "FTO Cupom";
            [MarshalAs(UnmanagedType.LPWStr)] public string pOutputFile = string.Empty;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDataType = "RAW";
        }

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFO di);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        public static void SendBytes(string printerName, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                throw new ArgumentException("Selecione uma impressora na tela de módulos.", nameof(printerName));

            if (data == null || data.Length == 0)
                throw new ArgumentException("Não há dados para imprimir.", nameof(data));

            if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Não foi possível abrir a impressora \"{printerName}\".");

            try
            {
                var di = new DOCINFO();
                if (!StartDocPrinter(hPrinter, 1, di))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao iniciar documento na impressora.");

                try
                {
                    if (!StartPagePrinter(hPrinter))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao iniciar página.");

                    try
                    {
                        IntPtr unmanaged = Marshal.AllocCoTaskMem(data.Length);
                        try
                        {
                            Marshal.Copy(data, 0, unmanaged, data.Length);
                            if (!WritePrinter(hPrinter, unmanaged, data.Length, out int written) || written != data.Length)
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao enviar dados para a impressora.");
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(unmanaged);
                        }
                    }
                    finally
                    {
                        EndPagePrinter(hPrinter);
                    }
                }
                finally
                {
                    EndDocPrinter(hPrinter);
                }
            }
            finally
            {
                ClosePrinter(hPrinter);
            }
        }
    }
}
