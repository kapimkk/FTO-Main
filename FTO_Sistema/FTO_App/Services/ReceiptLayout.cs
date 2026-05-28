using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FTO_App.Models;

namespace FTO_App.Services
{
    /// <summary>
    /// Cupom ESC/POS para MP-2500 HT (e compatíveis).
    /// </summary>
    public static class ReceiptLayout
    {
        private const string Separator = "-----------------------------------";
        private const int LineWidth = 35;
        private const int LeftPadding = 3;
        private const int SignatureBlankLines = 3;

        private static readonly Encoding EscEncoding;
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        private readonly record struct ReceiptLine(string Text, bool Center, bool IsBlank = false);

        static ReceiptLayout()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            EscEncoding = Encoding.GetEncoding(850);
        }

        public static string GetPreviewText(Venda venda, string? operador, bool incluirLinhaAssinatura)
        {
            var preview = new List<string>();
            foreach (ReceiptLine line in BuildReceiptLines(venda, operador, incluirLinhaAssinatura, DateTime.Now))
            {
                if (line.IsBlank)
                {
                    preview.Add(string.Empty);
                    continue;
                }

                preview.Add(FormatForPreview(line));
            }
            return string.Join(Environment.NewLine, preview);
        }

        public static byte[] BuildPrintData(Venda venda, string? operador, bool incluirLinhaAssinatura)
        {
            var buffer = new List<byte>();
            DateTime impressaoEm = DateTime.Now;

            buffer.AddRange(new byte[] { 0x1B, 0x40 });
            buffer.AddRange(new byte[] { 0x1B, 0x32 });
            buffer.Add(0x0A);
            buffer.AddRange(new byte[] { 0x1D, 0x21, 0x01 });

            foreach (ReceiptLine line in BuildReceiptLines(venda, operador, incluirLinhaAssinatura, impressaoEm))
                AppendLine(buffer, line);

            buffer.AddRange(new byte[] { 0x1D, 0x21, 0x00 });
            AppendAutoFeedAndCut(buffer);
            return buffer.ToArray();
        }

        private static List<ReceiptLine> BuildReceiptLines(Venda venda, string? operador, bool incluirAssinatura, DateTime impressaoEm)
        {
            var lines = new List<ReceiptLine>
            {
                Center("COMPROVANTE DE SERVICO"),
                Center("Documento auxiliar"),
                Center("Nao valido como documento fiscal"),
                Center(Separator),
                Center("FTO Informatica"),
                Center("Comprovante de servicos e vendas"),
                Center(Separator),
                Left("Rua Joao Bettega, 644"),
                Left("Portao - Curitiba/PR"),
                Left("CEP: 81070-000"),
                Left("Tel: (41) 98525-1213"),
                Center(Separator),
                Left("CNPJ: 13.416.624/0001-36"),
                Left("IE: 90622917-00"),
                Center(Separator),
                Left($"Impresso em: {impressaoEm.ToString("dd/MM/yyyy HH:mm:ss", PtBr)}"),
                Left("Informacoes do atendimento:"),
            };

            AddFieldLines(lines, "Data", venda.DataFormatada);
            AddFieldLines(lines, "Cliente", venda.Cliente);
            AddFieldLines(lines, "Contato", venda.Contato);
            AddFieldLines(lines, "CPF/CNPJ", venda.CPF_CNPJ);
            AddFieldLines(lines, "Servico", venda.TipoServico);
            AddFieldLines(lines, "Forma Pag.", venda.FormaPag);
            AddFieldLines(lines, "Valor", FormatMoney(venda.VendaValor));

            lines.Add(Center(Separator));

            if (incluirAssinatura)
            {
                if (!string.IsNullOrWhiteSpace(operador))
                    AddWrapped(lines, $"Operador: {operador.Trim()}", center: false);

                for (int i = 0; i < SignatureBlankLines; i++)
                    lines.Add(Blank());

                lines.Add(Center("___________________________________"));
                lines.Add(Blank());
                lines.Add(Blank());
                lines.Add(Center("Assinatura"));
                lines.Add(Blank());
                lines.Add(Center(Separator));
            }

            lines.Add(Center("Obrigado pela preferencia!"));
            return lines;
        }

        private static ReceiptLine Center(string text) => new(text, true);
        private static ReceiptLine Left(string text) => new(text, false);
        private static ReceiptLine Blank() => new(string.Empty, false, true);

        private static void AddFieldLines(List<ReceiptLine> lines, string label, string? value) =>
            AddWrapped(lines, FormatField(label, value), center: false);

        private static void AddWrapped(List<ReceiptLine> lines, string text, bool center)
        {
            foreach (string part in Wrap(text, center))
                lines.Add(center ? Center(part) : Left(part));
        }

        private static string FormatForPreview(ReceiptLine line)
        {
            if (line.Center)
                return CenterForPreview(line.Text);

            return new string(' ', LeftPadding) + line.Text;
        }

        private static string CenterForPreview(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Length >= LineWidth) return text;
            int pad = (LineWidth - text.Length) / 2;
            return new string(' ', pad) + text;
        }

        private static string FormatField(string label, string? value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
            return $"{label}: {value}";
        }

        private static string FormatMoney(decimal valor) =>
            $"R$ {valor.ToString("N2", PtBr)}";

        private static IEnumerable<string> Wrap(string text, bool centered)
        {
            text = Sanitize(text);
            int max = centered ? LineWidth : LineWidth - LeftPadding;

            if (text.Length <= max)
            {
                yield return text;
                yield break;
            }

            while (text.Length > max)
            {
                int breakAt = text.LastIndexOf(' ', max);
                if (breakAt <= 0) breakAt = max;
                yield return text[..breakAt].TrimEnd();
                text = text[breakAt..].TrimStart();
            }

            if (text.Length > 0)
                yield return text;
        }

        private static bool IsBoldTitle(string text) =>
            text is "COMPROVANTE DE SERVICO"
                or "FTO Informatica"
                or "Comprovante de servicos e vendas";

        private static void AppendLine(List<byte> buffer, ReceiptLine line)
        {
            if (line.IsBlank)
            {
                buffer.Add(0x0A);
                return;
            }

            string raw = Sanitize(line.Text);
            bool bold = line.Center && IsBoldTitle(raw);

            buffer.AddRange(new byte[] { 0x1B, 0x61, (byte)(line.Center ? 1 : 0) });
            if (bold) buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });

            foreach (string part in Wrap(raw, line.Center))
            {
                string printable = line.Center ? part : new string(' ', LeftPadding) + part;
                buffer.AddRange(EscEncoding.GetBytes(printable));
                buffer.Add(0x0A);
            }

            if (bold) buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });
            buffer.AddRange(new byte[] { 0x1B, 0x61, 0x00 });
        }

        private static void AppendAutoFeedAndCut(List<byte> buffer)
        {
            buffer.Add(0x0A);
            buffer.AddRange(new byte[] { 0x1B, 0x64, 0x0C });
            buffer.AddRange(new byte[] { 0x1B, 0x4A, 0x80 });
            buffer.AddRange(new byte[] { 0x1B, 0x69 });
            buffer.AddRange(new byte[] { 0x1D, 0x56, 0x00 });
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (char ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (ch is '—' or '–') { sb.Append('-'); continue; }
                if (ch is '’' or '‘') { sb.Append('\''); continue; }
                if (ch is '“' or '”') { sb.Append('"'); continue; }
                if (ch > 127) { sb.Append(MapExtendedChar(ch)); continue; }
                sb.Append(ch);
            }

            return sb.ToString();
        }

        private static char MapExtendedChar(char c) => c switch
        {
            'ç' or 'Ç' => 'c',
            'ã' or 'Ã' => 'a',
            'õ' or 'Õ' => 'o',
            'á' or 'Á' => 'a',
            'é' or 'É' => 'e',
            'í' or 'Í' => 'i',
            'ó' or 'Ó' => 'o',
            'ú' or 'Ú' => 'u',
            'â' or 'Â' => 'a',
            'ê' or 'Ê' => 'e',
            'ô' or 'Ô' => 'o',
            'à' or 'À' => 'a',
            _ => '?'
        };
    }
}
