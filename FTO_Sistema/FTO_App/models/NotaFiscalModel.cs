using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FTO_App.Models
{
    /// <summary>Campos do corpo NF-e alinhados ao contrato da API Fiscal (autorização).</summary>
    public class NotaFiscalModel
    {
        private static readonly JsonSerializerOptions JsonItens = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Itens (det) da nota — a fonte de verdade do que é emitido.
        ///
        /// Os campos Produto*/Icms*/Pis*/Cofins*/Ibs*/Cbs* abaixo são o formato ANTIGO (uma nota =
        /// um produto) e continuam existindo só por compatibilidade com as colunas da tabela:
        /// nota gravada antes dos itens é convertida por <see cref="GarantirItens"/>, e ao salvar
        /// o 1º item é espelhado neles para uma estação ainda na versão antiga não abrir a nota vazia.
        /// </summary>
        public List<NotaFiscalItemModel> Itens { get; set; } = new();

        public long Id { get; set; }
        public string NaturezaOperacao { get; set; } = "Venda de mercadoria";
        public string Modelo { get; set; } = "55"; // 55=NF-e
        public string Serie { get; set; } = "1";
        public long Numero { get; set; }
        public DateTime DataEmissao { get; set; } = DateTime.Now;
        public string TipoOperacao { get; set; } = "1"; // 0=Entrada, 1=Saída
        public string Finalidade { get; set; } = "1"; // 1=Normal
        public string ConsumidorFinal { get; set; } = "1";
        public string PresencaComprador { get; set; } = "1";
        public string Ambiente { get; set; } = "2"; // 1=Produção, 2=Homologação

        /// <summary>1=Interna, 2=Interestadual, 3=Exterior</summary>
        public string IdDest { get; set; } = "1";

        public long? ClienteId { get; set; }
        public string DestNome { get; set; } = string.Empty;
        public string DestCpfCnpj { get; set; } = string.Empty;
        public string DestIe { get; set; } = string.Empty;
        /// <summary>1=Contribuinte, 2=Isento, 9=Não contribuinte</summary>
        public string IndIEDest { get; set; } = "9";
        public string DestEmail { get; set; } = string.Empty;
        public string DestLogradouro { get; set; } = string.Empty;
        public string DestNumero { get; set; } = string.Empty;
        public string DestComplemento { get; set; } = string.Empty;
        public string DestBairro { get; set; } = string.Empty;
        public string DestMunicipio { get; set; } = string.Empty;
        public string DestUf { get; set; } = string.Empty;
        public string DestCep { get; set; } = string.Empty;
        public string DestCodigoIbge { get; set; } = string.Empty;

        public string ProdutoCodigo { get; set; } = string.Empty;
        public string ProdutoDescricao { get; set; } = string.Empty;
        public string ProdutoNcm { get; set; } = string.Empty;
        public string ProdutoCest { get; set; } = string.Empty;
        public string ProdutoGtin { get; set; } = "SEM GTIN";
        public string ProdutoCfop { get; set; } = "5102";
        public string ProdutoUnidade { get; set; } = "UN";
        public decimal ProdutoQuantidade { get; set; } = 1;
        public decimal ProdutoValorUnitario { get; set; }
        public decimal ProdutoValorTotal { get; set; }

        public string IcmsOrigem { get; set; } = "0";
        public string IcmsCst { get; set; } = "00";
        /// <summary>CSOSN (Simples Nacional) — usado quando CRT ≠ 3</summary>
        public string Csosn { get; set; } = "102";
        public decimal IcmsAliquota { get; set; }
        public decimal IcmsValor { get; set; }
        public string PisCst { get; set; } = "01";
        public decimal PisAliquota { get; set; }
        public decimal PisValor { get; set; }
        public string CofinsCst { get; set; } = "01";
        public decimal CofinsAliquota { get; set; }
        public decimal CofinsValor { get; set; }

        // IBS / CBS
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

        public decimal ValorProdutos { get; set; }
        public decimal ValorFrete { get; set; }
        public decimal ValorDesconto { get; set; }
        public decimal ValorTotalNota { get; set; }

        public string FormaPagamento { get; set; } = "01";
        public string InformacoesComplementares { get; set; } = string.Empty;
        public string Status { get; set; } = "Rascunho";
        public string CaminhoXml { get; set; } = string.Empty;

        // Resultado da última emissão/consulta na API Fiscal (PFCode)
        public string ChaveAcesso { get; set; } = string.Empty;
        public string NProt { get; set; } = string.Empty;
        public string DhRecbto { get; set; } = string.Empty;
        public string CStat { get; set; } = string.Empty;
        public string XMotivo { get; set; } = string.Empty;
        public string MensagemTraduzida { get; set; } = string.Empty;
        public string QrCodeUrl { get; set; } = string.Empty;
        public string XmlAutorizado { get; set; } = string.Empty;

        public bool TemChaveAcesso => !string.IsNullOrWhiteSpace(ChaveAcesso);

        /// <summary>
        /// Nota gravada antes do suporte a vários itens guarda o produto nos campos planos.
        /// Se não há itens, monta um a partir deles. Idempotente.
        /// </summary>
        public void GarantirItens()
        {
            if (Itens.Count > 0) return;

            bool temProduto = !string.IsNullOrWhiteSpace(ProdutoDescricao) ||
                              !string.IsNullOrWhiteSpace(ProdutoNcm) ||
                              ProdutoValorUnitario > 0 || ProdutoValorTotal > 0;
            if (!temProduto) return;

            Itens.Add(new NotaFiscalItemModel
            {
                Codigo = ProdutoCodigo,
                Descricao = ProdutoDescricao,
                Ncm = ProdutoNcm,
                Cest = ProdutoCest,
                Gtin = string.IsNullOrWhiteSpace(ProdutoGtin) ? "SEM GTIN" : ProdutoGtin,
                Cfop = ProdutoCfop,
                Unidade = ProdutoUnidade,
                Quantidade = ProdutoQuantidade,
                ValorUnitario = ProdutoValorUnitario,
                // Rascunho antigo podia ter ValorTotal=0 com qtd×unit preenchidos (MapRow antigo)
                ValorTotal = ProdutoValorTotal > 0
                    ? ProdutoValorTotal
                    : Math.Round(ProdutoQuantidade * ProdutoValorUnitario, 2),
                IcmsOrigem = IcmsOrigem,
                IcmsCst = IcmsCst,
                Csosn = Csosn,
                IcmsAliquota = IcmsAliquota,
                IcmsValor = IcmsValor,
                PisCst = PisCst,
                PisAliquota = PisAliquota,
                PisValor = PisValor,
                CofinsCst = CofinsCst,
                CofinsAliquota = CofinsAliquota,
                CofinsValor = CofinsValor,
                CstIbsCbs = CstIbsCbs,
                ClassTrib = ClassTrib,
                CbsAliquota = CbsAliquota,
                CbsValor = CbsValor,
                IbsAliquota = IbsAliquota,
                IbsValor = IbsValor,
                IbsAliquotaUf = IbsAliquotaUf,
                IbsValorUf = IbsValorUf,
                IbsAliquotaMun = IbsAliquotaMun,
                IbsValorMun = IbsValorMun
            });
        }

        /// <summary>
        /// Recalcula cada item e soma os totais da nota a partir dos valores JÁ arredondados de
        /// cada item (é assim que a SEFAZ confere). Depois espelha o 1º item nos campos legados.
        /// </summary>
        public void RecalcularTotais()
        {
            GarantirItens();
            foreach (var item in Itens)
                item.Recalcular();

            ValorProdutos = Itens.Sum(i => i.ValorTotal);
            ValorTotalNota = ValorProdutos + ValorFrete - ValorDesconto;

            IcmsValor = Itens.Sum(i => i.IcmsValor);
            PisValor = Itens.Sum(i => i.PisValor);
            CofinsValor = Itens.Sum(i => i.CofinsValor);
            CbsValor = Itens.Sum(i => i.CbsValor);
            IbsValor = Itens.Sum(i => i.IbsValor);
            IbsValorUf = Itens.Sum(i => i.IbsValorUf);
            IbsValorMun = Itens.Sum(i => i.IbsValorMun);

            EspelharPrimeiroItem();
        }

        private void EspelharPrimeiroItem()
        {
            var p = Itens.FirstOrDefault();
            if (p == null) return;

            ProdutoCodigo = p.Codigo;
            ProdutoDescricao = p.Descricao;
            ProdutoNcm = p.Ncm;
            ProdutoCest = p.Cest;
            ProdutoGtin = p.Gtin;
            ProdutoCfop = p.Cfop;
            ProdutoUnidade = p.Unidade;
            ProdutoQuantidade = p.Quantidade;
            ProdutoValorUnitario = p.ValorUnitario;
            ProdutoValorTotal = p.ValorTotal;
            IcmsOrigem = p.IcmsOrigem;
            IcmsCst = p.IcmsCst;
            Csosn = p.Csosn;
            IcmsAliquota = p.IcmsAliquota;
            PisCst = p.PisCst;
            PisAliquota = p.PisAliquota;
            CofinsCst = p.CofinsCst;
            CofinsAliquota = p.CofinsAliquota;
            CstIbsCbs = p.CstIbsCbs;
            ClassTrib = p.ClassTrib;
            CbsAliquota = p.CbsAliquota;
            IbsAliquota = p.IbsAliquota;
            IbsAliquotaUf = p.IbsAliquotaUf;
            IbsAliquotaMun = p.IbsAliquotaMun;
        }

        public string SerializarItens() => JsonSerializer.Serialize(Itens);

        /// <summary>Carrega os itens da coluna JSON. Vazio/inválido → cai no formato antigo.</summary>
        public void CarregarItens(string? json)
        {
            Itens = new List<NotaFiscalItemModel>();
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    Itens = JsonSerializer.Deserialize<List<NotaFiscalItemModel>>(json, JsonItens)
                            ?? new List<NotaFiscalItemModel>();
                }
                catch (JsonException)
                {
                    Itens = new List<NotaFiscalItemModel>();
                }
            }
            GarantirItens();
        }

        /// <summary>Resumo curto dos itens para cabeçalhos ("Parafuso M6" / "Parafuso M6 +2 itens").</summary>
        public string ResumoItens
        {
            get
            {
                if (Itens.Count == 0) return string.IsNullOrWhiteSpace(ProdutoDescricao) ? "(sem itens)" : ProdutoDescricao;
                string primeiro = Itens[0].Descricao;
                return Itens.Count == 1 ? primeiro : $"{primeiro} +{Itens.Count - 1} {(Itens.Count == 2 ? "item" : "itens")}";
            }
        }

        /// <summary>Rótulo amigável do modelo fiscal, usado na grade e nos títulos das janelas.</summary>
        public string ModeloExibicao => "NF-e";

        public string DataEmissaoFormatada => DataEmissao.ToString("dd/MM/yyyy HH:mm");
        public string ValorTotalFormatado => ValorTotalNota.ToString("C2");
        public string NumeroExibicao => $"{Serie}/{Numero}";

        /// <summary>Resumo para a grade: "cStat - xMotivo" quando já houve alguma tentativa de emissão via API.</summary>
        public string SituacaoFiscalExibicao => string.IsNullOrWhiteSpace(CStat)
            ? "—"
            : $"{CStat} - {(string.IsNullOrWhiteSpace(MensagemTraduzida) ? XMotivo : MensagemTraduzida)}";
    }
}
