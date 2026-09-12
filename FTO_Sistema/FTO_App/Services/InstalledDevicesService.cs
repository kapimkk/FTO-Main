using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Printing;

namespace FTO_App.Services
{
    /// <summary>
    /// Lista impressoras instaladas no Windows e dispositivos de digitalização (scanners).
    /// </summary>
    public static class InstalledDevicesService
    {
        /// <summary>
        /// Usa System.Printing (o mesmo do <see cref="CupomPrintHelper"/>) em vez de
        /// System.Drawing.Printing: o que a tela lista é exatamente o que a impressão consegue
        /// abrir, e o app deixa de arrastar System.Drawing.Common — que puxa todo o WinForms
        /// para o pacote self-contained (~21 MB de download em cada atualização).
        /// </summary>
        public static IReadOnlyList<string> GetPrinters()
        {
            var list = new List<string>();
            try
            {
                using var server = new LocalPrintServer();
                using var filas = server.GetPrintQueues(new[]
                {
                    EnumeratedPrintQueueTypes.Local,
                    EnumeratedPrintQueueTypes.Connections
                });

                foreach (PrintQueue fila in filas)
                {
                    using (fila)
                    {
                        if (!string.IsNullOrWhiteSpace(fila.FullName))
                            list.Add(fila.FullName);
                    }
                }
            }
            catch { }

            return list.Distinct(StringComparer.OrdinalIgnoreCase)
                       .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }

        public static IReadOnlyList<string> GetScanners()
        {
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Image' OR Name LIKE '%Scanner%' OR Name LIKE '%Digitaliz%'");

                foreach (ManagementObject device in searcher.Get())
                {
                    string? name = device["Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        list.Add(name.Trim());
                }
            }
            catch { }

            if (list.Count == 0)
                list.Add("(Nenhum scanner detectado)");

            return list.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Prioriza impressora MP-2500 HT quando disponível.
        /// </summary>
        public static string? FindPreferredThermalPrinter(IEnumerable<string> printers)
        {
            var all = printers.ToList();
            if (all.Count == 0) return null;

            string? mp = all.FirstOrDefault(p =>
                p.Contains("MP-2500", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("MP2500", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("MP 2500", StringComparison.OrdinalIgnoreCase));

            return mp ?? all.FirstOrDefault();
        }
    }
}
