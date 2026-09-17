using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace FTO_App.Models
{
    /// <summary>
    /// Um item (det) da NF-e: produto + tributos do item.
    ///
    /// Os valores monetários do item são arredondados AQUI, uma única vez, e os totais da nota
    /// são a soma desses valores já arredondados. A SEFAZ valida exatamente isso — vProd, vICMS,
    /// vPIS, vCOFINS, vIBS e vCBS do total precisam bater com o somatório dos itens (rejeições
    /// 531, 533, 564...). Calcular o total sobre a soma dos brutos e arredondar no fim dá um
    /// centavo de diferença com poucos itens.
    /// </summary>
    public class NotaFiscalItemModel
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        /// <summary>Produto do estoque de origem, quando o item veio de lá.</summary>
        public long? ProdutoId { get; set; }

        public string Codigo { get; set; } = string.Empty;
        public string Descricao { get; set; } = string.Empty;
        public string Ncm { get; set; } = string.Empty;
        public string Cest { get; set; } = string.Empty;
        public string Gtin { get; set; } = "SEM GTIN";
        public string Cfop { get; set; } = "5102";
        public string Unidade { get; set; } = "UN";
        public decimal Quantidade { get; set; } = 1;
        public decimal ValorUnitario { get; set; }
        public decimal ValorTotal { get; set; }

        public string IcmsOrigem { get; set; } = "0";
        public string IcmsCst { get; set; } = "00";
        /// <summary>CSOSN (Simples Nacional) — usado quando CRT ≠ 3.</summary>
        public string Csosn { get; set; } = "102";
        public decimal IcmsAliquota { get; set; }
        public decimal IcmsValor { get; set; }

        public string PisCst { get; set; } = "01";
        public decimal PisAliquota { get; set; }
        public decimal PisValor { get; set; }
        public string CofinsCst { get; set; } = "01";
        public decimal CofinsAliquota { get; set; }
        public decimal CofinsValor { get; set; }

        public string CstIbsCbs { get; set; } = "000";
        public string ClassTrib { get; set; } = "000001";
        public decimal CbsAliquota { get; set; }
        public decimal CbsValor { get; set; }
        public decimal IbsAliquota { get; set; }
        public decimal IbsValor { get; set; }
        public decimal IbsAliquotaUf { get; set; }
        public decimal IbsValorUf { get; set; }
        public decimal IbsAliquotaMun { get; set; }
        public decimal IbsValorMun { get; set; }

        /// <summary>
        /// Recalcula total e ICMS/PIS/COFINS a partir de quantidade, unitário e alíquotas.
        /// IBS/CBS não entram aqui: na emissão eles são recalculados com as alíquotas oficiais de
        /// transição (<c>ReformaTributariaService.CalcularParaEmissao</c>).
        /// </summary>
        public void Recalcular()
        {
            ValorTotal = Math.Round(Quantidade * ValorUnitario, 2);
            IcmsValor = Math.Round(ValorTotal * IcmsAliquota / 100m, 2);
            PisValor = Math.Round(ValorTotal * PisAliquota / 100m, 2);
            CofinsValor = Math.Round(ValorTotal * CofinsAliquota / 100m, 2);
        }

        public NotaFiscalItemModel Clonar() => (NotaFiscalItemModel)MemberwiseClone();

        // --- exibição na grade de itens (fora do JSON persistido) ---

        /// <summary>nItem exibido na grade; renumerado pela tela a cada inclusão/remoção.</summary>
        [JsonIgnore]
        public int Posicao { get; set; }

        [JsonIgnore]
        public string QuantidadeFormatada => Quantidade.ToString("0.####", PtBr);

        [JsonIgnore]
        public string ValorUnitarioFormatado => ValorUnitario.ToString("C2", PtBr);

        [JsonIgnore]
        public string ValorTotalFormatado => ValorTotal.ToString("C2", PtBr);
    }
}
