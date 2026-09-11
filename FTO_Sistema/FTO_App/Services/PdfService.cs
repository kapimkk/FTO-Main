using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FTO_App.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FTO_App.Services
{
    /// <summary>Geração de PDFs (lista de clientes, cupom não fiscal e DANFE NF-e com logo).</summary>
    public static class PdfService
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        static PdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        /// <summary>
        /// DANFE auxiliar A4 da NF-e (modelo 55) com logo do emitente, quando configurada.
        /// Complementa o PDF oficial da API — não substitui o XML autorizado.
        /// </summary>
        public static void GerarDanfeNfeComLogo(NotaFiscalModel nota, EmpresaConfig empresa, string caminhoArquivo)
        {
            if (nota is null) throw new ArgumentNullException(nameof(nota));
            if (empresa is null) throw new ArgumentNullException(nameof(empresa));
            if (string.IsNullOrWhiteSpace(caminhoArquivo)) throw new ArgumentException("Caminho inválido.", nameof(caminhoArquivo));

            string nomeEmit = string.IsNullOrWhiteSpace(empresa.NomeFantasia) ? empresa.Nome : empresa.NomeFantasia;
            string logoPath = empresa.LogoPath?.Trim() ?? "";
            bool temLogo = !string.IsNullOrWhiteSpace(logoPath) && System.IO.File.Exists(logoPath);

            Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            if (temLogo)
                            {
                                row.ConstantItem(90).Height(56).Image(logoPath).FitArea();
                                row.ConstantItem(12);
                            }
                            row.RelativeItem().Column(info =>
                            {
                                info.Item().Text(nomeEmit).Bold().FontSize(14);
                                if (!string.IsNullOrWhiteSpace(empresa.RazaoSocial) &&
                                    !string.Equals(empresa.RazaoSocial, nomeEmit, StringComparison.OrdinalIgnoreCase))
                                    info.Item().Text(empresa.RazaoSocial).FontSize(9);
                                if (!string.IsNullOrWhiteSpace(empresa.CnpjExibicao))
                                    info.Item().Text(empresa.CnpjExibicao).FontSize(9);
                                if (!string.IsNullOrWhiteSpace(empresa.IeExibicao))
                                    info.Item().Text(empresa.IeExibicao).FontSize(9);
                                string end = string.Join(", ", new[] { empresa.Endereco, empresa.Numero, empresa.Bairro }
                                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                                if (!string.IsNullOrWhiteSpace(end))
                                    info.Item().Text(end).FontSize(8);
                                if (!string.IsNullOrWhiteSpace(empresa.Cidade))
                                    info.Item().Text($"{empresa.Cidade}/{empresa.Uf}").FontSize(8);
                            });
                        });
                        col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                        col.Item().PaddingTop(6).AlignCenter().Text("DANFE — Documento Auxiliar da Nota Fiscal Eletrônica")
                            .Bold().FontSize(12);
                        col.Item().AlignCenter().Text("Modelo 55 · Uso interno / impressão com logo do emitente").FontSize(8).Italic();
                    });

                    page.Content().PaddingTop(14).Column(col =>
                    {
                        col.Spacing(6);
                        if (nota.Ambiente != "1")
                            col.Item().Background(Colors.Amber.Lighten3).Padding(6)
                                .Text("AMBIENTE DE HOMOLOGAÇÃO — SEM VALOR FISCAL").Bold().FontSize(10);

                        col.Item().Text($"NF-e nº {nota.Numero}  ·  Série {nota.Serie}  ·  Emissão {nota.DataEmissao:dd/MM/yyyy HH:mm}").SemiBold();
                        if (!string.IsNullOrWhiteSpace(nota.ChaveAcesso))
                            col.Item().Text($"Chave de acesso: {nota.ChaveAcesso}").FontSize(9);
                        if (!string.IsNullOrWhiteSpace(nota.NProt))
                            col.Item().Text($"Protocolo: {nota.NProt}").FontSize(9);

                        col.Item().PaddingTop(8).Text("DESTINATÁRIO").Bold().FontSize(11);
                        col.Item().Text(string.IsNullOrWhiteSpace(nota.DestNome) ? "Não identificado" : nota.DestNome);
                        if (!string.IsNullOrWhiteSpace(nota.DestCpfCnpj))
                            col.Item().Text($"CPF/CNPJ: {nota.DestCpfCnpj}").FontSize(9);

                        col.Item().PaddingTop(8).Text("PRODUTO / SERVIÇO").Bold().FontSize(11);
                        col.Item().Text(string.IsNullOrWhiteSpace(nota.ProdutoDescricao) ? "-" : nota.ProdutoDescricao);
                        col.Item().Text(
                            $"Qtd {nota.ProdutoQuantidade.ToString("0.####", PtBr)} {nota.ProdutoUnidade}  ×  " +
                            $"{nota.ProdutoValorUnitario.ToString("C2", PtBr)}  =  {nota.ProdutoValorTotal.ToString("C2", PtBr)}");

                        col.Item().PaddingTop(10).Row(r =>
                        {
                            r.RelativeItem().Text("VALOR TOTAL DA NOTA").Bold().FontSize(12);
                            r.ConstantItem(120).AlignRight().Text(nota.ValorTotalNota.ToString("C2", PtBr)).Bold().FontSize(14);
                        });

                        col.Item().PaddingTop(16).Text(
                            "Este PDF inclui a logo do emitente para identificação visual. " +
                            "O XML autorizado pela SEFAZ permanece o documento fiscal válido.")
                            .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                    });

                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("Gerado pelo FTO  ·  ");
                        t.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm", PtBr));
                    });
                });
            }).GeneratePdf(caminhoArquivo);
        }

        public static void GerarListaClientesPdf(IReadOnlyList<ClienteModel> clientes, string caminhoArquivo)
        {
            if (clientes is null) throw new ArgumentNullException(nameof(clientes));
            if (string.IsNullOrWhiteSpace(caminhoArquivo)) throw new ArgumentException("Caminho inválido.", nameof(caminhoArquivo));

            var empresa = EmpresaConfigStore.Current;

            Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Column(col =>
                    {
                        col.Item().Text(empresa.Nome).Bold().FontSize(16);
                        col.Item().PaddingTop(6).Text($"Lista de Clientes — {DateTime.Now:dd/MM/yyyy HH:mm}").SemiBold();
                        col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    });

                    page.Content().PaddingTop(12).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(45);
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CabecalhoCelula).Text("ID");
                            header.Cell().Element(CabecalhoCelula).Text("Nome");
                            header.Cell().Element(CabecalhoCelula).Text("Contato");
                            header.Cell().Element(CabecalhoCelula).Text("CPF/CNPJ");
                        });

                        foreach (ClienteModel cliente in clientes.OrderBy(c => c.Nome))
                        {
                            table.Cell().Element(CorpoCelula).Text(cliente.Id.ToString(PtBr));
                            table.Cell().Element(CorpoCelula).Text(cliente.Nome);
                            table.Cell().Element(CorpoCelula).Text(string.IsNullOrWhiteSpace(cliente.Contato) ? "-" : cliente.Contato);
                            table.Cell().Element(CorpoCelula).Text(string.IsNullOrWhiteSpace(cliente.CpfCnpj) ? "-" : cliente.CpfCnpj);
                        }
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Total: ").SemiBold();
                        text.Span($"{clientes.Count} cliente(s)");
                        text.Span("  |  Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf(caminhoArquivo);
        }

        public static void GerarCupomPdf(Venda venda, string caminhoArquivo)
        {
            if (venda is null) throw new ArgumentNullException(nameof(venda));
            if (string.IsNullOrWhiteSpace(caminhoArquivo)) throw new ArgumentException("Caminho inválido.", nameof(caminhoArquivo));

            var empresa = EmpresaConfigStore.Current;
            string impressoEm = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", PtBr);
            string servico = string.IsNullOrWhiteSpace(venda.TipoServico) ? "-" : venda.TipoServico.Trim();
            string formaPag = string.IsNullOrWhiteSpace(venda.FormaPag) ? "-" : venda.FormaPag.Trim();
            string cliente = string.IsNullOrWhiteSpace(venda.Cliente) ? "-" : venda.Cliente.Trim();

            Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.ContinuousSize(80, Unit.Millimetre);
                    page.MarginVertical(8, Unit.Millimetre);
                    page.MarginHorizontal(6, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Content().Column(col =>
                    {
                        col.Spacing(3);

                        col.Item().AlignCenter().Column(blocoEmpresa =>
                        {
                            blocoEmpresa.Spacing(1);
                            blocoEmpresa.Item().Text(empresa.Nome).Bold().FontSize(11);
                            if (!string.IsNullOrWhiteSpace(empresa.Subtitulo))
                                blocoEmpresa.Item().Text(empresa.Subtitulo).SemiBold().FontSize(8);

                            if (!string.IsNullOrWhiteSpace(empresa.Endereco))
                                blocoEmpresa.Item().Text(empresa.Endereco).FontSize(8);
                            if (!string.IsNullOrWhiteSpace(empresa.Cidade))
                                blocoEmpresa.Item().Text(empresa.Cidade).FontSize(8);
                            if (!string.IsNullOrWhiteSpace(empresa.TelefoneExibicao))
                                blocoEmpresa.Item().Text(empresa.TelefoneExibicao).FontSize(8);
                            if (!string.IsNullOrWhiteSpace(empresa.CnpjExibicao))
                                blocoEmpresa.Item().Text(empresa.CnpjExibicao).FontSize(8);
                            if (!string.IsNullOrWhiteSpace(empresa.IeExibicao))
                                blocoEmpresa.Item().Text(empresa.IeExibicao).FontSize(8);

                            blocoEmpresa.Item().PaddingTop(2).Text(empresa.CupomTitulo).Bold().FontSize(8);
                        });

                        col.Item().PaddingTop(6).PaddingBottom(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);

                        col.Item().Element(c => LinhaRotuloValor(c, "Venda Nº:", venda.Id.ToString(PtBr)));
                        col.Item().Element(c => LinhaRotuloValor(c, "Data:", venda.DataFormatada));
                        col.Item().Element(c => LinhaRotuloValor(c, "Impresso em:", impressoEm));
                        col.Item().Element(c => LinhaRotuloValor(c, "Cliente:", cliente));

                        col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);

                        col.Item().Text("Itens / descrição:").SemiBold().FontSize(8);
                        col.Item().Text(servico).SemiBold().FontSize(8);

                        col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                        col.Item().LineHorizontal(1.5f).LineColor("#1e3a5f");

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("TOTAL:").Bold().FontSize(10);
                            row.ConstantItem(100).AlignRight().Text(venda.VendaValor.ToString("C2", PtBr)).Bold().FontSize(12);
                        });

                        col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);

                        col.Item().Text("PAGAMENTO").Bold().FontSize(8);
                        col.Item().Element(c => LinhaRotuloValor(c, "Forma:", formaPag));

                        col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);

                        if (!string.IsNullOrWhiteSpace(empresa.CupomRodape))
                        {
                            col.Item().AlignCenter().Text(empresa.CupomRodape).Italic().FontSize(8);
                        }
                    });
                });
            }).GeneratePdf(caminhoArquivo);
        }

        private static void LinhaRotuloValor(IContainer container, string rotulo, string valor)
        {
            container.Row(row =>
            {
                row.AutoItem().Text(rotulo).SemiBold();
                row.ConstantItem(4);
                row.RelativeItem().Text(valor);
            });
        }

        private static IContainer CabecalhoCelula(IContainer container) =>
            container.DefaultTextStyle(x => x.SemiBold())
                .Background(Colors.Grey.Lighten3)
                .Border(0.5f)
                .BorderColor(Colors.Grey.Medium)
                .PaddingVertical(6)
                .PaddingHorizontal(4);

        private static IContainer CorpoCelula(IContainer container) =>
            container.Border(0.5f)
                .BorderColor(Colors.Grey.Lighten2)
                .PaddingVertical(5)
                .PaddingHorizontal(4);
    }
}
