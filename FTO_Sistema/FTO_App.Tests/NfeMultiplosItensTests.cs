using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FTO_App.Models;
using FTO_App.Services;

namespace FTO_App.Tests;

/// <summary>
/// NF-e com vários itens. O ponto que a SEFAZ mais rejeita é o total não fechar com a soma dos
/// itens (531/533/564) — por isso cada teste de total compara o grupo total com o somatório do
/// que foi efetivamente enviado em cada det, não com um valor calculado à parte.
/// </summary>
public class NfeMultiplosItensTests
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    private static EmpresaConfig Emitente(string crt) => new()
    {
        Nome = "FTO Teste",
        RazaoSocial = "FTO Teste Ltda",
        Cnpj = "13416624000136",
        Ie = "9062291700",
        Uf = "PR",
        CodigoIbge = "4106902",
        Cidade = "Curitiba",
        Endereco = "Rua Teste",
        Bairro = "Centro",
        Cep = "81070000",
        RegimeTributario = crt
    };

    private static NotaFiscalItemModel Item(string desc, decimal qtd, decimal unit,
        decimal icms = 0, decimal pis = 0, decimal cofins = 0, string cstIcms = "00", string cstPis = "01") => new()
    {
        Codigo = desc[..3].ToUpperInvariant(),
        Descricao = desc,
        Ncm = "84713012",
        Cfop = "5102",
        Unidade = "UN",
        Quantidade = qtd,
        ValorUnitario = unit,
        IcmsCst = cstIcms,
        IcmsAliquota = icms,
        PisCst = cstPis,
        PisAliquota = pis,
        CofinsCst = cstPis,
        CofinsAliquota = cofins
    };

    private static NotaFiscalModel Nota(string ambiente, params NotaFiscalItemModel[] itens) => new()
    {
        Numero = 10,
        Ambiente = ambiente,
        DestNome = "Cliente Teste",
        DestCpfCnpj = "12345678000195",
        DestUf = "PR",
        DestCodigoIbge = "4106902",
        FormaPagamento = "01",
        Itens = itens.ToList()
    };

    private static JsonArray Dets(JsonObject payload) => payload["infNFe"]!["det"]!.AsArray();
    private static JsonObject IcmsTot(JsonObject payload) => payload["infNFe"]!["total"]!["ICMSTot"]!.AsObject();
    private static decimal D(JsonNode? n) => n?.GetValue<decimal>() ?? 0m;

    [Fact]
    public void Payload_UmDetPorItem_ComNItemSequencial()
    {
        var nota = Nota("1", Item("Mouse", 2, 50), Item("Teclado", 1, 120), Item("Monitor", 1, 900));

        var dets = Dets(FiscalPayloadBuilder.BuildEmissao(nota, Emitente("1")));

        Assert.Equal(3, dets.Count);
        Assert.Equal(new[] { "1", "2", "3" }, dets.Select(d => d!["nItem"]!.GetValue<string>()));
        Assert.Equal(new[] { "Mouse", "Teclado", "Monitor" },
            dets.Select(d => d!["prod"]!["xProd"]!.GetValue<string>()));
    }

    [Fact]
    public void Payload_RegimeNormal_TotaisSaoASomaExataDosItens()
    {
        // Alíquotas quebradas para forçar arredondamento por item.
        var nota = Nota("1",
            Item("Cabo HDMI", 3, 19.99m, icms: 18, pis: 1.65m, cofins: 7.6m),
            Item("Fonte ATX", 1, 249.90m, icms: 12, pis: 1.65m, cofins: 7.6m),
            Item("Pasta termica", 7, 3.33m, icms: 7, pis: 1.65m, cofins: 7.6m));

        var payload = FiscalPayloadBuilder.BuildEmissao(nota, Emitente("3"));
        var dets = Dets(payload);
        var tot = IcmsTot(payload);

        Assert.Equal(dets.Sum(d => D(d!["prod"]!["vProd"])), D(tot["vProd"]));
        Assert.Equal(dets.Sum(d => D(d!["imposto"]!["ICMS"]!["icmsDetails"]!["vBC"])), D(tot["vBC"]));
        Assert.Equal(dets.Sum(d => D(d!["imposto"]!["ICMS"]!["icmsDetails"]!["vICMS"])), D(tot["vICMS"]));
        Assert.Equal(dets.Sum(d => D(d!["imposto"]!["PIS"]!["pisDetails"]!["vPIS"])), D(tot["vPIS"]));
        Assert.Equal(dets.Sum(d => D(d!["imposto"]!["COFINS"]!["cofinsDetails"]!["vCOFINS"])), D(tot["vCOFINS"]));

        // 3×19,99 + 249,90 + 7×3,33 = 59,97 + 249,90 + 23,31
        Assert.Equal(333.18m, D(tot["vProd"]));
        Assert.Equal(D(tot["vProd"]), D(tot["vNF"]));

        var vPag = D(payload["infNFe"]!["pag"]!["detPag"]![0]!["vPag"]);
        Assert.Equal(D(tot["vNF"]), vPag);
    }

    [Fact]
    public void Payload_IbsCbsTotal_SomaDosGruposDosItens()
    {
        var nota = Nota("1", Item("Mouse", 3, 33.33m), Item("Teclado", 1, 99.99m));

        var payload = FiscalPayloadBuilder.BuildEmissao(nota, Emitente("1"));
        var dets = Dets(payload);
        var tot = payload["infNFe"]!["total"]!["IBSCBSTot"]!;

        decimal Soma(Func<JsonNode, JsonNode?> campo) =>
            dets.Sum(d => D(campo(d!["imposto"]!["IBSCBS"]!["tribDetails"]!)));

        Assert.Equal(Soma(t => t["vBC"]), D(tot["vBCIBSCBS"]));
        Assert.Equal(Soma(t => t["gIBSUF"]!["vIBSUF"]), D(tot["gIBS"]!["gIBSUF"]!["vIBSUF"]));
        Assert.Equal(Soma(t => t["gIBSMun"]!["vIBSMun"]), D(tot["gIBS"]!["gIBSMun"]!["vIBSMun"]));
        Assert.Equal(Soma(t => t["vIBS"]), D(tot["gIBS"]!["vIBS"]));
        Assert.Equal(Soma(t => t["gCBS"]!["vCBS"]), D(tot["gCBS"]!["vCBS"]));
    }

    [Fact]
    public void Payload_Homologacao_SoOPrimeiroItemRecebeOTextoFixo()
    {
        var nota = Nota("2", Item("Mouse", 1, 50), Item("Teclado", 1, 120));

        var dets = Dets(FiscalPayloadBuilder.BuildEmissao(nota, Emitente("1")));

        Assert.Equal(FiscalHomologacaoTextos.XProd, dets[0]!["prod"]!["xProd"]!.GetValue<string>());
        Assert.Equal("Teclado", dets[1]!["prod"]!["xProd"]!.GetValue<string>());
    }

    [Fact]
    public void Payload_RegimeNormal_Cst40NaoEntraNaBaseDoTotal()
    {
        var nota = Nota("1",
            Item("Tributado", 1, 100, icms: 18, cstIcms: "00"),
            Item("Isento", 1, 50, icms: 18, cstIcms: "40"));

        var payload = FiscalPayloadBuilder.BuildEmissao(nota, Emitente("3"));
        var icmsIsento = Dets(payload)[1]!["imposto"]!["ICMS"]!["icmsDetails"]!.AsObject();
        var tot = IcmsTot(payload);

        Assert.False(icmsIsento.ContainsKey("vBC"));
        Assert.Equal(100m, D(tot["vBC"]));
        Assert.Equal(18m, D(tot["vICMS"]));
        Assert.Equal(150m, D(tot["vProd"]));
    }

    [Fact]
    public void Payload_PisNaoTributado_NaoSomaNoTotal()
    {
        var nota = Nota("1",
            Item("Tributado", 1, 100, pis: 1.65m, cofins: 7.6m, cstPis: "01"),
            // Alíquota preenchida por engano num CST não tributado não pode vazar para o total.
            Item("Monofasico", 1, 100, pis: 1.65m, cofins: 7.6m, cstPis: "04"));

        var tot = IcmsTot(FiscalPayloadBuilder.BuildEmissao(nota, Emitente("1")));

        Assert.Equal(1.65m, D(tot["vPIS"]));
        Assert.Equal(7.60m, D(tot["vCOFINS"]));
    }

    [Fact]
    public void Payload_SimplesNacional_IcmsTotZerado()
    {
        var nota = Nota("1", Item("Mouse", 1, 100, icms: 18), Item("Teclado", 1, 50, icms: 18));

        var tot = IcmsTot(FiscalPayloadBuilder.BuildEmissao(nota, Emitente("1")));

        Assert.Equal(0m, D(tot["vBC"]));
        Assert.Equal(0m, D(tot["vICMS"]));
    }

    [Fact]
    public void Payload_NotaSemItens_Lanca()
    {
        var nota = Nota("1");

        Assert.Throws<InvalidOperationException>(() => FiscalPayloadBuilder.BuildEmissao(nota, Emitente("1")));
    }

    [Fact]
    public void Xml_UmDetPorItem_ETotaisBatemComOsItens()
    {
        var nota = Nota("1",
            Item("Cabo HDMI", 3, 19.99m, icms: 18),
            Item("Isento", 1, 50, icms: 18, cstIcms: "41"));

        var doc = XDocument.Parse(NfeXmlService.GerarXml(nota, Emitente("3")));
        var dets = doc.Descendants(Nfe + "det").ToList();
        var tot = doc.Descendants(Nfe + "ICMSTot").Single();

        decimal Xd(XElement? e) => decimal.Parse(e!.Value, CultureInfo.InvariantCulture);

        Assert.Equal(new[] { "1", "2" }, dets.Select(d => d.Attribute("nItem")!.Value));
        Assert.Equal(dets.Sum(d => Xd(d.Descendants(Nfe + "vProd").Single())), Xd(tot.Element(Nfe + "vProd")));
        // CST 41 sai como ICMS40, sem base — o total só pode levar a base do item tributado.
        Assert.Single(dets[1].Descendants(Nfe + "ICMS40"));
        Assert.Equal(59.97m, Xd(tot.Element(Nfe + "vBC")));
        Assert.Equal(dets.Sum(d => d.Descendants(Nfe + "vICMS").Select(Xd).Sum()), Xd(tot.Element(Nfe + "vICMS")));
    }

    [Fact]
    public void NotaAntiga_SemItens_ViraUmItemComOsMesmosValores()
    {
        var nota = new NotaFiscalModel
        {
            ProdutoCodigo = "P01",
            ProdutoDescricao = "Notebook",
            ProdutoNcm = "84713012",
            ProdutoCfop = "5102",
            ProdutoUnidade = "UN",
            ProdutoQuantidade = 2,
            ProdutoValorUnitario = 1500m,
            // Rascunho antigo com total zerado (bug do MapRow antigo)
            ProdutoValorTotal = 0,
            IcmsCst = "00",
            IcmsAliquota = 12
        };

        nota.CarregarItens(json: "");

        var item = Assert.Single(nota.Itens);
        Assert.Equal("Notebook", item.Descricao);
        Assert.Equal(2, item.Quantidade);
        Assert.Equal(3000m, item.ValorTotal);
        Assert.Equal(12, item.IcmsAliquota);
    }

    [Fact]
    public void NotaVazia_NaoInventaItem()
    {
        var nota = new NotaFiscalModel();

        nota.GarantirItens();

        Assert.Empty(nota.Itens);
    }

    [Fact]
    public void Itens_SerializamEVoltamIguais()
    {
        var original = Nota("1", Item("Mouse", 2, 49.9m, icms: 18), Item("Teclado", 1, 120, pis: 1.65m));
        original.Itens[0].ProdutoId = 42;
        original.RecalcularTotais();

        var carregada = new NotaFiscalModel();
        carregada.CarregarItens(original.SerializarItens());

        Assert.Equal(2, carregada.Itens.Count);
        Assert.Equal(42, carregada.Itens[0].ProdutoId);
        Assert.Equal(99.8m, carregada.Itens[0].ValorTotal);
        Assert.Equal(17.96m, carregada.Itens[0].IcmsValor);
        Assert.Equal("Teclado", carregada.Itens[1].Descricao);
        Assert.Equal(1.65m, carregada.Itens[1].PisAliquota);
    }

    [Fact]
    public void CarregarItens_JsonCorrompido_CaiNoFormatoAntigo()
    {
        var nota = new NotaFiscalModel { ProdutoDescricao = "Mouse", ProdutoQuantidade = 1, ProdutoValorUnitario = 10 };

        nota.CarregarItens("{ isso não é json");

        Assert.Equal("Mouse", Assert.Single(nota.Itens).Descricao);
    }

    [Fact]
    public void RecalcularTotais_EspelhaOPrimeiroItemNosCamposLegados()
    {
        var nota = Nota("1", Item("Mouse", 2, 50), Item("Teclado", 1, 120));

        nota.RecalcularTotais();

        Assert.Equal(220m, nota.ValorProdutos);
        Assert.Equal(220m, nota.ValorTotalNota);
        Assert.Equal("Mouse", nota.ProdutoDescricao);
        Assert.Equal(100m, nota.ProdutoValorTotal);
        Assert.Equal("Mouse +1 item", nota.ResumoItens);
    }

    [Fact]
    public void Validacao_ApontaQualItemTemProblema()
    {
        var bom = Item("Mouse", 1, 50);
        var ruim = Item("Teclado", 0, 0);
        ruim.Ncm = "";
        ruim.Cfop = "51";

        var erros = NotaFiscalValidacao.ValidarItens(new[] { bom, ruim });

        var erro = Assert.Single(erros);
        Assert.StartsWith("Item 2 (Teclado)", erro);
        Assert.Contains("NCM", erro);
        Assert.Contains("CFOP", erro);
        Assert.Contains("quantidade", erro);
        Assert.Contains("valor unitário", erro);
    }

    /// <summary>
    /// O texto do PDF sai comprimido, então aqui não dá para procurar as descrições. O que o teste
    /// trava é o layout: tabela com várias linhas e colunas de largura fixa não pode estourar
    /// (o QuestPDF lança na geração quando o conteúdo não cabe).
    /// </summary>
    [Fact]
    public void DanfeLocal_TabelaDeItensGeraSemErroDeLayout()
    {
        var nota = Nota("2", Item("Monitor", 2, 899.9m), Item("Teclado", 5, 249m), Item("Cabo HDMI", 10, 39.9m));
        nota.RecalcularTotais();
        string pdf = Path.Combine(Path.GetTempPath(), $"danfe_teste_{Guid.NewGuid():N}.pdf");

        try
        {
            PdfService.GerarDanfeNfeComLogo(nota, Emitente("1"), pdf);

            var info = new FileInfo(pdf);
            Assert.True(info.Exists && info.Length > 1000, "PDF não foi gerado");
        }
        finally
        {
            if (File.Exists(pdf)) File.Delete(pdf);
        }
    }

    [Fact]
    public void Validacao_NotaSemItens()
    {
        Assert.Contains("pelo menos um item", Assert.Single(NotaFiscalValidacao.ValidarItens(Array.Empty<NotaFiscalItemModel>())));
    }
}
