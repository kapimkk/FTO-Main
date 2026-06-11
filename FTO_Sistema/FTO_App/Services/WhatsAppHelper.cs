using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FTO_App.Services
{
    /// <summary>
    /// Abre conversa no WhatsApp Desktop via protocolo whatsapp:// (fallback wa.me).
    /// </summary>
    public static class WhatsAppHelper
    {
        private static readonly Regex DigitosRegex = new(@"\D", RegexOptions.Compiled);

        public static bool TryNormalizePhone(string? raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string digits = DigitosRegex.Replace(raw.Trim(), "");
            if (digits.Length == 0)
                return false;

            if (digits.StartsWith("00", StringComparison.Ordinal))
                digits = digits.Substring(2);

            // Brasil: DDD + número sem código do país.
            if (digits.Length is 10 or 11 && !digits.StartsWith("55", StringComparison.Ordinal))
                digits = "55" + digits;

            // 55 + DDD (2) + número (8 ou 9 dígitos).
            if (digits.Length < 12 || digits.Length > 13)
                return false;

            if (!digits.StartsWith("55", StringComparison.Ordinal))
                return false;

            normalized = digits;
            return true;
        }

        public static bool OpenChat(string? contato, out string? mensagemErro)
        {
            mensagemErro = null;

            if (!TryNormalizePhone(contato, out string phone))
            {
                mensagemErro = "Esta venda não possui um número de contato válido para WhatsApp.\n\n" +
                               "Verifique o campo Contato (ex.: (41) 98525-1213).";
                return false;
            }

            string deepLink = $"whatsapp://send?phone={phone}";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = deepLink,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception exDeepLink)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"https://wa.me/{phone}",
                        UseShellExecute = true
                    });
                    return true;
                }
                catch (Exception exWeb)
                {
                    mensagemErro = "Não foi possível abrir o WhatsApp.\n\n" +
                                   "Confirme que o WhatsApp Desktop está instalado.\n\n" +
                                   $"Detalhe: {exDeepLink.Message}\n{exWeb.Message}";
                    return false;
                }
            }
        }
    }
}
