using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FTO_App.Models;

namespace FTO_App.Services
{
    /// <summary>
    /// Gera o corpo XML de NF-e alinhado ao contrato da API Fiscal (GUIA_INTEGRACAO §4 / §4.10).
    /// Não assina nem transmite — a API calcula chave/cNF, assina e monta infRespTec.
    /// </summary>
    public static class NfeXmlService
    {
        private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

        /// <summary>Mantido por compatibilidade — usar <see cref="FiscalHomologacaoTextos.XNomeDest"/>.</summary>
        public const string NomeDestHomologacao = FiscalHomologacaoTextos.XNomeDest;

        public static string GerarXml(NotaFiscalModel nota, EmpresaConfig emitente)
        {
            ArgumentNullException.ThrowIfNull(nota);
            ArgumentNullException.ThrowIfNull(emitente);

            nota.Modelo = "55";
            string cnpjEmit = SomenteDigitos(emitente.Cnpj);
            string docDest = SomenteDigitos(nota.DestCpfCnpj);
            bool destPj = docDest.Length > 11;
            string ambiente = string.IsNullOrWhiteSpace(nota.Ambiente) ? "2" : nota.Ambiente.Trim();
            bool homolog = ambiente == "2";

            string destNome = homolog ? NomeDestHomologacao : nota.DestNome;

            nota.RecalcularTotais();
            if (nota.Itens.Count == 0)
                throw new InvalidOperationException("A NF-e não tem itens.");

            string idDest = string.IsNullOrWhiteSpace(nota.IdDest)
                ? InferirIdDest(nota.Itens[0].Cfop, emitente.Uf, nota.DestUf)
                : nota.IdDest.Trim();

            var (indIEDest, ieDest) = ConciliarIndIeDest(nota.IndIEDest, nota.DestIe);
            nota.IndIEDest = indIEDest;
            nota.DestIe = ieDest;
            string crt = string.IsNullOrWhiteSpace(emitente.RegimeTributario) ? "1" : emitente.RegimeTributario.Trim();

            var ide = new XElement(Nfe + "ide",
                El("cUF", UfToCodigo(emitente.Uf)),
                El("natOp", nota.NaturezaOperacao),
                El("mod", "55"),
                El("serie", nota.Serie),
                El("nNF", nota.Numero.ToString(CultureInfo.InvariantCulture)),
                El("dhEmi", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")),
                El("tpNF", nota.TipoOperacao),
                El("idDest", idDest),
                El("cMunFG", emitente.CodigoIbge),
                El("tpImp", "1"),
                El("tpEmis", "1"),
                El("tpAmb", ambiente),
                El("finNFe", nota.Finalidade),
                El("indFinal", nota.ConsumidorFinal),
                El("indPres", nota.PresencaComprador),
                El("procEmi", "0"),
                El("verProc", "FTO_1.0")
            );

            var enderEmit = new XElement(Nfe + "enderEmit",
                El("xLgr", emitente.Endereco),
                El("nro", string.IsNullOrWhiteSpace(emitente.Numero) ? "S/N" : emitente.Numero),
                Opt("xCpl", emitente.Complemento),
                El("xBairro", emitente.Bairro),
                El("cMun", emitente.CodigoIbge),
                El("xMun", emitente.Cidade),
                El("UF", emitente.Uf),
                El("CEP", SomenteDigitos(emitente.Cep)),
                El("cPais", "1058"),
                El("xPais", "Brasil"),
                Opt("fone", SomenteDigitos(emitente.Telefone))
            );

            var emit = new XElement(Nfe + "emit",
                El("CNPJ", cnpjEmit),
                El("xNome", string.IsNullOrWhiteSpace(emitente.RazaoSocial) ? emitente.Nome : emitente.RazaoSocial),
                Opt("xFant", string.IsNullOrWhiteSpace(emitente.NomeFantasia) ? emitente.Subtitulo : emitente.NomeFantasia),
                enderEmit,
                El("IE", SomenteDigitosOuIsento(emitente.Ie)),
                El("CRT", crt)
            );

            var enderDest = new XElement(Nfe + "enderDest",
                El("xLgr", nota.DestLogradouro),
                El("nro", string.IsNullOrWhiteSpace(nota.DestNumero) ? "S/N" : nota.DestNumero),
                Opt("xCpl", nota.DestComplemento),
                El("xBairro", nota.DestBairro),
                El("cMun", nota.DestCodigoIbge),
                El("xMun", nota.DestMunicipio),
                El("UF", nota.DestUf),
                El("CEP", SomenteDigitos(nota.DestCep)),
                El("cPais", "1058"),
                El("xPais", "Brasil")
            );

            var dest = new XElement(Nfe + "dest",
                destPj ? El("CNPJ", docDest) : El("CPF", docDest),
                El("xNome", destNome),
                enderDest,
                El("indIEDest", indIEDest),
                indIEDest == "1" ? El("IE", ieDest) : null,
                indIEDest == "2" ? El("IE", "ISENTO") : null,
                Opt("email", nota.DestEmail)
            );

            // Mesmos valores efetivos por item da emissão via API — XML e JSON não podem divergir.
            var itens = FiscalPayloadBuilder.ItemEmissao.Montar(nota, crt);
            var dets = itens.Select(e => MontarDet(e, crt, homolog)).ToList();

            decimal vProd = itens.Sum(i => i.Item.ValorTotal);
            var total = new XElement(Nfe + "total",
                new XElement(Nfe + "ICMSTot",
                    El("vBC", Dec(itens.Sum(i => i.VBcIcms))),
                    El("vICMS", Dec(itens.Sum(i => i.VIcms))),
                    El("vICMSDeson", "0.00"),
                    El("vFCP", "0.00"),
                    El("vBCST", "0.00"),
                    El("vST", "0.00"),
                    El("vFCPST", "0.00"),
                    El("vFCPSTRet", "0.00"),
                    El("vProd", Dec(vProd)),
                    El("vFrete", Dec(nota.ValorFrete)),
                    El("vSeg", "0.00"),
                    El("vDesc", Dec(nota.ValorDesconto)),
                    El("vII", "0.00"),
                    El("vIPI", "0.00"),
                    El("vIPIDevol", "0.00"),
                    El("vPIS", Dec(itens.Sum(i => i.VPis))),
                    El("vCOFINS", Dec(itens.Sum(i => i.VCofins))),
                    El("vOutro", "0.00"),
                    El("vNF", Dec(vProd + nota.ValorFrete - nota.ValorDesconto))
                ),
                MontarIbsCbsTot(itens)
            );

            string tPag = string.IsNullOrWhiteSpace(nota.FormaPagamento) ? "01" : nota.FormaPagamento.Trim();
            var detPag = new XElement(Nfe + "detPag",
                El("tPag", tPag),
                El("vPag", Dec(nota.ValorTotalNota))
            );
            if (tPag is "03" or "04" or "17")
                detPag.Add(new XElement(Nfe + "card", El("tpIntegra", "2")));

            var pag = new XElement(Nfe + "pag", detPag);

            var infNFe = new XElement(Nfe + "infNFe", new XAttribute("versao", "4.00"),
                ide, emit, dest, dets, total,
                new XElement(Nfe + "transp", El("modFrete", "9")),
                pag,
                string.IsNullOrWhiteSpace(nota.InformacoesComplementares)
                    ? null
                    : new XElement(Nfe + "infAdic", El("infCpl", nota.InformacoesComplementares))
            );

            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(Nfe + "NFe", infNFe)
            );

            return doc.ToString(SaveOptions.DisableFormatting);
        }

        public static string SalvarXml(NotaFiscalModel nota, EmpresaConfig emitente, string? pasta = null)
        {
            pasta ??= Path.Combine(AppContext.BaseDirectory, "xml_nfe");
            Directory.CreateDirectory(pasta);
            string xml = GerarXml(nota, emitente);
            string file = Path.Combine(pasta, $"NFe_{nota.Serie}_{nota.Numero}_{DateTime.Now:yyyyMMddHHmmss}.xml");
            File.WriteAllText(file, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return file;
        }

        /// <summary>
        /// Em homologação (tpAmb=2), a SEFAZ exige que xProd do primeiro item seja EXATAMENTE
        /// <see cref="FiscalHomologacaoTextos.XProd"/> (Rejeição 373) — nunca a descrição real,
        /// nem um sufixo/variação dela. Fora de homologação, retorna a descrição normalmente.
        /// </summary>
        public static string AplicarHomologDescricao(string? descricao, bool homolog) =>
            homolog ? FiscalHomologacaoTextos.XProd : (descricao ?? "").Trim();

        public static string InferirIdDest(string? cfop, string? ufEmit, string? ufDest)
        {
            string c = (cfop ?? "").Trim();
            if (c.Length >= 1)
            {
                char d = c[0];
                if (d is '2' or '6') return "2";
                if (d is '3' or '7') return "3";
                if (d is '1' or '5') return "1";
            }

            string a = (ufEmit ?? "").Trim().ToUpperInvariant();
            string b = (ufDest ?? "").Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && a != b)
                return "2";
            return "1";
        }

        private static XElement MontarDet(FiscalPayloadBuilder.ItemEmissao e, string crt, bool homolog)
        {
            var item = e.Item;
            string gtin = string.IsNullOrWhiteSpace(item.Gtin) || item.Gtin == "SEM GTIN" ? "SEM GTIN" : item.Gtin;

            // Rejeição 373: em homologação só o xProd do PRIMEIRO item precisa ser o texto fixo.
            string xProd = e.NItem == 1
                ? AplicarHomologDescricao(item.Descricao, homolog)
                : (item.Descricao ?? "").Trim();

            var prod = new XElement(Nfe + "prod",
                El("cProd", string.IsNullOrWhiteSpace(item.Codigo) ? e.NItem.ToString("000", CultureInfo.InvariantCulture) : item.Codigo),
                El("cEAN", gtin),
                El("xProd", xProd),
                El("NCM", ReformaTributariaService.NormalizarNcm(item.Ncm)));
            string cest = SomenteDigitos(item.Cest);
            if (!string.IsNullOrEmpty(cest))
                prod.Add(El("CEST", cest));
            prod.Add(
                El("CFOP", item.Cfop),
                El("uCom", item.Unidade),
                El("qCom", Dec(item.Quantidade, "0.####")),
                El("vUnCom", Dec(item.ValorUnitario)),
                El("vProd", Dec(item.ValorTotal)),
                El("cEANTrib", gtin),
                El("uTrib", item.Unidade),
                El("qTrib", Dec(item.Quantidade, "0.####")),
                El("vUnTrib", Dec(item.ValorUnitario)),
                El("indTot", "1")
            );

            return new XElement(Nfe + "det", new XAttribute("nItem", e.NItem.ToString(CultureInfo.InvariantCulture)),
                prod,
                new XElement(Nfe + "imposto",
                    MontarIcms(e, crt),
                    MontarPis(e),
                    MontarCofins(e),
                    MontarIbsCbsItem(e.IbsCbs)
                )
            );
        }

        private static XElement MontarIcms(FiscalPayloadBuilder.ItemEmissao e, string crt)
        {
            var item = e.Item;
            string orig = string.IsNullOrWhiteSpace(item.IcmsOrigem) ? "0" : item.IcmsOrigem.Trim();

            // Regime normal (CRT=3): CST
            if (crt == "3")
            {
                // 40/41/50 não têm base — mandar ICMS00 com vBC fazia o total divergir dos itens.
                if (FiscalPayloadBuilder.IcmsSemBase(e.CstIcms))
                {
                    return new XElement(Nfe + "ICMS",
                        new XElement(Nfe + "ICMS40", El("orig", orig), El("CST", e.CstIcms)));
                }

                return new XElement(Nfe + "ICMS",
                    new XElement(Nfe + "ICMS00",
                        El("orig", orig),
                        El("CST", e.CstIcms),
                        El("modBC", "3"),
                        El("vBC", Dec(e.VBcIcms)),
                        El("pICMS", Dec(item.IcmsAliquota)),
                        El("vICMS", Dec(e.VIcms))
                    )
                );
            }

            // Simples / MEI: CSOSN
            return new XElement(Nfe + "ICMS", MontarIcmsSn(orig, e.Csosn, item));
        }

        private static XElement MontarIcmsSn(string orig, string csosn, NotaFiscalItemModel item)
        {
            return csosn switch
            {
                "101" => new XElement(Nfe + "ICMSSN101",
                    El("orig", orig),
                    El("CSOSN", csosn),
                    El("pCredSN", Dec(item.IcmsAliquota)),
                    El("vCredICMSSN", Dec(item.IcmsValor))),
                "201" => new XElement(Nfe + "ICMSSN201",
                    El("orig", orig),
                    El("CSOSN", csosn),
                    El("modBCST", "4"),
                    El("pMVAST", "0.00"),
                    El("vBCST", "0.00"),
                    El("pICMSST", "0.00"),
                    El("vICMSST", "0.00"),
                    El("pCredSN", Dec(item.IcmsAliquota)),
                    El("vCredICMSSN", Dec(item.IcmsValor))),
                "202" or "203" => new XElement(Nfe + "ICMSSN202",
                    El("orig", orig),
                    El("CSOSN", csosn),
                    El("modBCST", "4"),
                    El("vBCST", "0.00"),
                    El("pICMSST", "0.00"),
                    El("vICMSST", "0.00")),
                "500" => new XElement(Nfe + "ICMSSN500",
                    El("orig", orig),
                    El("CSOSN", csosn),
                    El("vBCSTRet", "0.00"),
                    El("pST", "0.00"),
                    El("vICMSSTRet", "0.00")),
                "900" => new XElement(Nfe + "ICMSSN900",
                    El("orig", orig),
                    El("CSOSN", csosn)),
                // 102, 103, 300, 400 e demais sem crédito
                _ => new XElement(Nfe + "ICMSSN102",
                    El("orig", orig),
                    El("CSOSN", csosn is "102" or "103" or "300" or "400" ? csosn : "102"))
            };
        }

        private static XElement MontarPis(FiscalPayloadBuilder.ItemEmissao e)
        {
            if (FiscalPayloadBuilder.PisCofinsNaoTributado(e.CstPis))
            {
                return new XElement(Nfe + "PIS",
                    new XElement(Nfe + "PISNT", El("CST", e.CstPis)));
            }

            return new XElement(Nfe + "PIS",
                new XElement(Nfe + "PISAliq",
                    El("CST", e.CstPis),
                    El("vBC", Dec(e.Item.ValorTotal)),
                    El("pPIS", Dec(e.Item.PisAliquota)),
                    El("vPIS", Dec(e.VPis))
                ));
        }

        private static XElement MontarCofins(FiscalPayloadBuilder.ItemEmissao e)
        {
            if (FiscalPayloadBuilder.PisCofinsNaoTributado(e.CstCofins))
            {
                return new XElement(Nfe + "COFINS",
                    new XElement(Nfe + "COFINSNT", El("CST", e.CstCofins)));
            }

            return new XElement(Nfe + "COFINS",
                new XElement(Nfe + "COFINSAliq",
                    El("CST", e.CstCofins),
                    El("vBC", Dec(e.Item.ValorTotal)),
                    El("pCOFINS", Dec(e.Item.CofinsAliquota)),
                    El("vCOFINS", Dec(e.VCofins))
                ));
        }

        /// <summary>
        /// Estrutura oficial do item: gIBSCBS → vBC, gIBSUF, gIBSMun, vIBS, gCBS
        /// (sem wrapper gIBS no item — ver DFeTiposBasicos / API §4.10).
        /// Usa as mesmas alíquotas de transição da API (CalcularParaEmissao) para não divergir do JSON.
        /// </summary>
        private static XElement MontarIbsCbsItem(ReformaTributariaService.Resultado r)
        {
            return new XElement(Nfe + "IBSCBS",
                El("CST", r.Cst),
                El("cClassTrib", r.ClassTrib),
                new XElement(Nfe + "gIBSCBS",
                    El("vBC", Dec(r.BaseCalculo)),
                    new XElement(Nfe + "gIBSUF",
                        El("pIBSUF", Dec(r.AliquotaIbsUf, "0.####")),
                        El("vIBSUF", Dec(r.ValorIbsUf))
                    ),
                    new XElement(Nfe + "gIBSMun",
                        El("pIBSMun", Dec(r.AliquotaIbsMun, "0.####")),
                        El("vIBSMun", Dec(r.ValorIbsMun))
                    ),
                    El("vIBS", Dec(r.ValorIbs)),
                    new XElement(Nfe + "gCBS",
                        El("pCBS", Dec(r.AliquotaCbs, "0.####")),
                        El("vCBS", Dec(r.ValorCbs))
                    )
                )
            );
        }

        private static XElement MontarIbsCbsTot(IReadOnlyList<FiscalPayloadBuilder.ItemEmissao> itens)
        {
            return new XElement(Nfe + "IBSCBSTot",
                El("vBCIBSCBS", Dec(itens.Sum(i => i.IbsCbs.BaseCalculo))),
                new XElement(Nfe + "gIBS",
                    new XElement(Nfe + "gIBSUF",
                        El("vDif", "0.00"),
                        El("vDevTrib", "0.00"),
                        El("vIBSUF", Dec(itens.Sum(i => i.IbsCbs.ValorIbsUf)))
                    ),
                    new XElement(Nfe + "gIBSMun",
                        El("vDif", "0.00"),
                        El("vDevTrib", "0.00"),
                        El("vIBSMun", Dec(itens.Sum(i => i.IbsCbs.ValorIbsMun)))
                    ),
                    El("vIBS", Dec(itens.Sum(i => i.IbsCbs.ValorIbs))),
                    El("vCredPres", "0.00"),
                    El("vCredPresCondSus", "0.00")
                ),
                new XElement(Nfe + "gCBS",
                    El("vDif", "0.00"),
                    El("vDevTrib", "0.00"),
                    El("vCBS", Dec(itens.Sum(i => i.IbsCbs.ValorCbs))),
                    El("vCredPres", "0.00"),
                    El("vCredPresCondSus", "0.00")
                )
            );
        }

        /// <summary>
        /// Normaliza e concilia indIEDest × IE (evita rejeição 232 e cadastro inconsistente).
        /// Regras SEFAZ: 1=Contribuinte (IE obrigatória), 2=Isento (IE=ISENTO), 9=Não contribuinte (sem IE).
        /// Se a IE tiver dígitos, força indIEDest=1 mesmo que a tela esteja em 9.
        /// </summary>
        public static (string IndIEDest, string Ie) ConciliarIndIeDest(string? ind, string? ie)
        {
            string ieBruto = (ie ?? "").Trim();
            bool isento = string.Equals(ieBruto, "ISENTO", StringComparison.OrdinalIgnoreCase);
            string ieDigits = isento ? "" : SomenteDigitos(ieBruto);

            if (isento)
                return ("2", "ISENTO");

            // IE numérica preenchida → contribuinte ICMS (rejeição 232 se mandar ind=1 sem IE;
            // ind=9 com IE preenchida é inconsistente e a IE seria omitida no XML)
            if (ieDigits.Length > 0)
                return ("1", ieDigits);

            string v = (ind ?? "").Trim();
            if (v == "1")
                return ("1", ""); // inválido sem IE — ValidarIndIeDest bloqueia antes da SEFAZ
            if (v == "2")
                return ("2", "ISENTO");
            return ("9", "");
        }

        /// <summary>Mantido por compatibilidade — preferir <see cref="ConciliarIndIeDest"/>.</summary>
        public static string NormalizarIndIEDest(string? ind, string? ie) =>
            ConciliarIndIeDest(ind, ie).IndIEDest;

        /// <summary>
        /// Retorna mensagem de erro se indIEDest/IE estiverem inválidos para emissão; null se ok.
        /// </summary>
        public static string? ValidarIndIeDest(string? ind, string? ie)
        {
            var (indOk, ieOk) = ConciliarIndIeDest(ind, ie);
            if (indOk == "1" && string.IsNullOrWhiteSpace(ieOk))
                return "IE do destinatário é obrigatória quando indIEDest = 1 (Contribuinte).\n\n" +
                       "Informe a Inscrição Estadual ou altere para \"9-Não contribuinte\" / \"2-Isento\".\n" +
                       "(SEFAZ rejeição 232)";
            return null;
        }

        private static XElement El(string name, string? value) =>
            new(Nfe + name, value ?? "");

        private static XElement? Opt(string name, string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : new XElement(Nfe + name, value.Trim());

        private static string Dec(decimal v, string format = "0.00") =>
            v.ToString(format, CultureInfo.InvariantCulture);

        private static string SomenteDigitos(string? s) =>
            string.IsNullOrWhiteSpace(s) ? "" : new string(Array.FindAll(s.ToCharArray(), char.IsDigit));

        private static string SomenteDigitosOuIsento(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            if (string.Equals(s.Trim(), "ISENTO", StringComparison.OrdinalIgnoreCase)) return "ISENTO";
            return SomenteDigitos(s);
        }

        private static string UfToCodigo(string? uf) => (uf ?? "").Trim().ToUpperInvariant() switch
        {
            "AC" => "12", "AL" => "27", "AP" => "16", "AM" => "13", "BA" => "29",
            "CE" => "23", "DF" => "53", "ES" => "32", "GO" => "52", "MA" => "21",
            "MT" => "51", "MS" => "50", "MG" => "31", "PA" => "15", "PB" => "25",
            "PR" => "41", "PE" => "26", "PI" => "22", "RJ" => "33", "RN" => "24",
            "RS" => "43", "RO" => "11", "RR" => "14", "SC" => "42", "SP" => "35",
            "SE" => "28", "TO" => "17",
            _ => "41"
        };
    }
}
