using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using FTO_App.Models;

namespace FTO_App.Services
{
    /// <summary>
    /// Monta o corpo JSON de emissão (POST /emitir) exigido pela API Fiscal PFCode para NF-e (mod 55),
    /// a partir do NotaFiscalModel + EmpresaConfig — mesma decisão fiscal já usada em
    /// <see cref="NfeXmlService"/> (CST×CSOSN por CRT, PIS/COFINS NT, grupo IBSCBS), mas serializada como
    /// JSON e não como XML (a API monta/assina/transmite o XML no servidor).
    ///
    /// IMPORTANTE — grupos "choice" do XSD (ICMS/PIS/COFINS/IBSCBS): a API resolve o tipo concreto pelo
    /// conteúdo de um wrapper (icmsDetails/pisDetails/cofinsDetails/tribDetails), lendo CST/CSOSN de
    /// dentro dele (ver Fiscal.Shared.Services.JsonToXsdResolverService). Não existe endpoint que aceite
    /// "ICMS00"/"ICMSSN102" como chave — sempre use o wrapper "*Details" com os campos linearizados.
    /// </summary>
    public static class FiscalPayloadBuilder
    {
        /// <summary>Mantido por compatibilidade — usar <see cref="FiscalHomologacaoTextos.XNomeDest"/>.</summary>
        public const string NomeDestHomologacao = FiscalHomologacaoTextos.XNomeDest;

        public static JsonObject BuildEmissao(NotaFiscalModel nota, EmpresaConfig emitente)
        {
            ArgumentNullException.ThrowIfNull(nota);
            ArgumentNullException.ThrowIfNull(emitente);

            string ambiente = FiscalApiClient.NormalizarTpAmb(nota.Ambiente);
            bool homolog = ambiente == "2";
            string crt = string.IsNullOrWhiteSpace(emitente.RegimeTributario) ? "1" : emitente.RegimeTributario.Trim();

            string cnpjEmit = SomenteDigitos(emitente.Cnpj);
            string docDest = SomenteDigitos(nota.DestCpfCnpj);
            bool destPj = docDest.Length > 11;

            nota.RecalcularTotais();
            if (nota.Itens.Count == 0)
                throw new InvalidOperationException("A NF-e não tem itens.");

            string idDest = string.IsNullOrWhiteSpace(nota.IdDest)
                ? NfeXmlService.InferirIdDest(nota.Itens[0].Cfop, emitente.Uf, nota.DestUf)
                : nota.IdDest.Trim();

            nota.Modelo = "55";

            var infNFe = new JsonObject
            {
                ["versao"] = "4.00",
                ["ide"] = MontarIde(nota, emitente, idDest, ambiente),
                ["emit"] = MontarEmit(emitente, cnpjEmit, crt),
                ["dest"] = MontarDest(nota, docDest, destPj, homolog)
            };

            var itens = ItemEmissao.Montar(nota, crt);
            var det = new JsonArray();
            foreach (var item in itens)
                det.Add(MontarDet(item, crt, homolog));

            infNFe["det"] = det;
            infNFe["total"] = MontarTotal(nota, itens);
            infNFe["transp"] = new JsonObject { ["modFrete"] = "9" };
            infNFe["pag"] = MontarPag(nota);

            if (!string.IsNullOrWhiteSpace(nota.InformacoesComplementares))
                infNFe["infAdic"] = new JsonObject { ["infCpl"] = nota.InformacoesComplementares.Trim() };

            return new JsonObject { ["infNFe"] = infNFe };
        }

        private static JsonObject MontarIde(NotaFiscalModel nota, EmpresaConfig emitente, string idDest, string ambiente)
        {
            return new JsonObject
            {
                ["cUF"] = UfToCodigo(emitente.Uf),
                ["natOp"] = nota.NaturezaOperacao,
                ["mod"] = "55",
                ["serie"] = nota.Serie,
                ["nNF"] = nota.Numero.ToString(CultureInfo.InvariantCulture),
                ["dhEmi"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                ["tpNF"] = nota.TipoOperacao,
                ["idDest"] = idDest,
                ["cMunFG"] = emitente.CodigoIbge,
                ["tpImp"] = "1",
                ["tpEmis"] = "1",
                ["tpAmb"] = ambiente,
                ["finNFe"] = nota.Finalidade,
                ["indFinal"] = nota.ConsumidorFinal,
                ["indPres"] = nota.PresencaComprador,
                ["procEmi"] = "0",
                ["verProc"] = "FTO_1.0"
            };
        }

        private static JsonObject MontarEmit(EmpresaConfig emitente, string cnpjEmit, string crt)
        {
            var enderEmit = new JsonObject
            {
                ["xLgr"] = emitente.Endereco,
                ["nro"] = string.IsNullOrWhiteSpace(emitente.Numero) ? "S/N" : emitente.Numero,
                ["xBairro"] = emitente.Bairro,
                ["cMun"] = emitente.CodigoIbge,
                ["xMun"] = emitente.Cidade,
                ["UF"] = emitente.Uf,
                ["CEP"] = SomenteDigitos(emitente.Cep),
                ["cPais"] = "1058",
                ["xPais"] = "BRASIL"
            };
            if (!string.IsNullOrWhiteSpace(emitente.Complemento)) enderEmit["xCpl"] = emitente.Complemento.Trim();
            string fone = SomenteDigitos(emitente.Telefone);
            if (!string.IsNullOrEmpty(fone)) enderEmit["fone"] = fone;

            var emit = new JsonObject
            {
                ["CNPJ"] = cnpjEmit,
                ["xNome"] = string.IsNullOrWhiteSpace(emitente.RazaoSocial) ? emitente.Nome : emitente.RazaoSocial,
                ["enderEmit"] = enderEmit,
                ["IE"] = SomenteDigitosOuIsento(emitente.Ie),
                ["CRT"] = crt
            };
            string xFant = string.IsNullOrWhiteSpace(emitente.NomeFantasia) ? emitente.Subtitulo : emitente.NomeFantasia;
            if (!string.IsNullOrWhiteSpace(xFant)) emit["xFant"] = xFant.Trim();
            return emit;
        }

        private static JsonObject MontarDest(NotaFiscalModel nota, string docDest, bool destPj, bool homolog)
        {
            string destNome = homolog ? NomeDestHomologacao : nota.DestNome;
            var (indIEDest, ieDest) = NfeXmlService.ConciliarIndIeDest(nota.IndIEDest, nota.DestIe);
            nota.IndIEDest = indIEDest;
            nota.DestIe = ieDest;

            var enderDest = new JsonObject
            {
                ["xLgr"] = nota.DestLogradouro,
                ["nro"] = string.IsNullOrWhiteSpace(nota.DestNumero) ? "S/N" : nota.DestNumero,
                ["xBairro"] = nota.DestBairro,
                ["cMun"] = nota.DestCodigoIbge,
                ["xMun"] = nota.DestMunicipio,
                ["UF"] = nota.DestUf,
                ["CEP"] = SomenteDigitos(nota.DestCep)
            };
            if (!string.IsNullOrWhiteSpace(nota.DestComplemento)) enderDest["xCpl"] = nota.DestComplemento.Trim();

            var dest = new JsonObject { ["xNome"] = destNome };
            if (!string.IsNullOrEmpty(docDest))
                dest[destPj ? "CNPJ" : "CPF"] = docDest;
            dest["enderDest"] = enderDest;
            dest["indIEDest"] = indIEDest;
            // Rejeição 232: indIEDest=1 exige o elemento IE
            if (indIEDest == "1") dest["IE"] = ieDest;
            if (indIEDest == "2") dest["IE"] = "ISENTO";
            if (!string.IsNullOrWhiteSpace(nota.DestEmail)) dest["email"] = nota.DestEmail.Trim();
            return dest;
        }

        /// <summary>
        /// Valores efetivos de cada item na emissão, calculados UMA vez e reaproveitados no det e
        /// no total — assim o total é, por construção, a soma exata do que foi enviado nos itens.
        /// </summary>
        internal sealed class ItemEmissao
        {
            public required NotaFiscalItemModel Item { get; init; }
            public required int NItem { get; init; }
            public required ReformaTributariaService.Resultado IbsCbs { get; init; }
            public required string CstIcms { get; init; }
            public required string Csosn { get; init; }
            public required string CstPis { get; init; }
            public required string CstCofins { get; init; }
            /// <summary>vBC/vICMS que entram no item E no ICMSTot (zero sem base ou no Simples).</summary>
            public required decimal VBcIcms { get; init; }
            public required decimal VIcms { get; init; }
            public required decimal VPis { get; init; }
            public required decimal VCofins { get; init; }

            public static List<ItemEmissao> Montar(NotaFiscalModel nota, string crt)
            {
                bool regimeNormal = crt == "3";
                var lista = new List<ItemEmissao>(nota.Itens.Count);

                for (int i = 0; i < nota.Itens.Count; i++)
                {
                    var item = nota.Itens[i];
                    string cstIcms = string.IsNullOrWhiteSpace(item.IcmsCst) ? "00" : item.IcmsCst.Trim();
                    string csosn = string.IsNullOrWhiteSpace(item.Csosn) ? "102" : item.Csosn.Trim();
                    string cstPis = string.IsNullOrWhiteSpace(item.PisCst) ? "01" : item.PisCst.Trim();
                    string cstCofins = string.IsNullOrWhiteSpace(item.CofinsCst) ? "01" : item.CofinsCst.Trim();

                    // Rejeição 564: vICMS deve fechar Base × Alíquota do próprio item.
                    bool comBaseIcms = regimeNormal && !IcmsSemBase(cstIcms);

                    lista.Add(new ItemEmissao
                    {
                        Item = item,
                        NItem = i + 1,
                        IbsCbs = ReformaTributariaService.CalcularParaEmissao(item),
                        CstIcms = cstIcms,
                        Csosn = csosn,
                        CstPis = cstPis,
                        CstCofins = cstCofins,
                        VBcIcms = comBaseIcms ? item.ValorTotal : 0m,
                        VIcms = comBaseIcms ? item.IcmsValor : 0m,
                        // CST não tributado não leva vPIS/vCOFINS no item — então não pode somar no total.
                        VPis = PisCofinsNaoTributado(cstPis) ? 0m : item.PisValor,
                        VCofins = PisCofinsNaoTributado(cstCofins) ? 0m : item.CofinsValor
                    });
                }

                return lista;
            }
        }

        internal static bool IcmsSemBase(string cst) => cst is "40" or "41" or "50";

        internal static bool PisCofinsNaoTributado(string cst) =>
            cst is "04" or "05" or "06" or "07" or "08" or "09";

        private static JsonObject MontarDet(ItemEmissao e, string crt, bool homolog)
        {
            var item = e.Item;
            string gtin = string.IsNullOrWhiteSpace(item.Gtin) || item.Gtin == "SEM GTIN" ? "SEM GTIN" : item.Gtin;
            string ncm = ReformaTributariaService.NormalizarNcm(item.Ncm);

            // Rejeição 373: em homologação só o xProd do PRIMEIRO item precisa ser o texto fixo.
            string xProd = e.NItem == 1
                ? NfeXmlService.AplicarHomologDescricao(item.Descricao, homolog)
                : (item.Descricao ?? "").Trim();

            var prod = new JsonObject
            {
                ["cProd"] = string.IsNullOrWhiteSpace(item.Codigo) ? e.NItem.ToString("000", CultureInfo.InvariantCulture) : item.Codigo,
                ["cEAN"] = gtin,
                ["xProd"] = xProd,
                // NCM vazio falha no XSD (pattern) — validação prévia em NotaFiscalAcoesWindow
                ["NCM"] = ncm
            };
            string cest = SomenteDigitos(item.Cest);
            if (!string.IsNullOrEmpty(cest)) prod["CEST"] = cest;
            prod["CFOP"] = item.Cfop;
            prod["uCom"] = item.Unidade;
            prod["qCom"] = N(item.Quantidade, 4);
            prod["vUnCom"] = N(item.ValorUnitario, 4);
            prod["vProd"] = N(item.ValorTotal);
            prod["cEANTrib"] = gtin;
            prod["uTrib"] = item.Unidade;
            prod["qTrib"] = N(item.Quantidade, 4);
            prod["vUnTrib"] = N(item.ValorUnitario, 4);
            prod["indTot"] = "1";

            var imposto = new JsonObject
            {
                ["ICMS"] = new JsonObject { ["icmsDetails"] = MontarIcmsDetails(e, crt) },
                ["PIS"] = new JsonObject { ["pisDetails"] = MontarPisDetails(e) },
                ["COFINS"] = new JsonObject { ["cofinsDetails"] = MontarCofinsDetails(e) },
                ["IBSCBS"] = MontarIbsCbsItem(e.IbsCbs)
            };

            return new JsonObject
            {
                ["nItem"] = e.NItem.ToString(CultureInfo.InvariantCulture),
                ["prod"] = prod,
                ["imposto"] = imposto
            };
        }

        private static JsonObject MontarIcmsDetails(ItemEmissao e, string crt)
        {
            var item = e.Item;
            string orig = string.IsNullOrWhiteSpace(item.IcmsOrigem) ? "0" : item.IcmsOrigem.Trim();

            if (crt == "3")
            {
                var o = new JsonObject { ["orig"] = orig, ["CST"] = e.CstIcms };
                if (!IcmsSemBase(e.CstIcms))
                {
                    o["modBC"] = "3";
                    o["vBC"] = N(e.VBcIcms);
                    o["pICMS"] = N(item.IcmsAliquota, 4);
                    o["vICMS"] = N(e.VIcms);
                }
                return o;
            }

            var r = new JsonObject { ["orig"] = orig, ["CSOSN"] = e.Csosn };
            if (e.Csosn == "101")
            {
                r["pCredSN"] = N(item.IcmsAliquota, 4);
                r["vCredICMSSN"] = N(item.IcmsValor);
            }
            return r;
        }

        private static JsonObject MontarPisDetails(ItemEmissao e)
        {
            var o = new JsonObject { ["CST"] = e.CstPis };
            if (PisCofinsNaoTributado(e.CstPis)) return o;
            o["vBC"] = N(e.Item.ValorTotal);
            o["pPIS"] = N(e.Item.PisAliquota, 4);
            o["vPIS"] = N(e.VPis);
            return o;
        }

        private static JsonObject MontarCofinsDetails(ItemEmissao e)
        {
            var o = new JsonObject { ["CST"] = e.CstCofins };
            if (PisCofinsNaoTributado(e.CstCofins)) return o;
            o["vBC"] = N(e.Item.ValorTotal);
            o["pCOFINS"] = N(e.Item.CofinsAliquota, 4);
            o["vCOFINS"] = N(e.VCofins);
            return o;
        }

        /// <summary>
        /// Grupo IBSCBS do item — obrigatório desde 2026 (cStat 1115 se ausente).
        /// Alíquotas forçadas pela NT 2025.002 no ano da emissão (rejeição 1026 se pIBSUF ≠ 0,1% em 2026).
        /// cClassTrib normalizado para 6 dígitos (XSD TcClassTrib rejeita "0").
        /// </summary>
        private static JsonObject MontarIbsCbsItem(ReformaTributariaService.Resultado r)
        {
            return new JsonObject
            {
                ["CST"] = r.Cst,
                ["cClassTrib"] = r.ClassTrib,
                ["tribDetails"] = new JsonObject
                {
                    ["vBC"] = N(r.BaseCalculo),
                    ["gIBSUF"] = new JsonObject { ["pIBSUF"] = N(r.AliquotaIbsUf, 4), ["vIBSUF"] = N(r.ValorIbsUf) },
                    ["gIBSMun"] = new JsonObject { ["pIBSMun"] = N(r.AliquotaIbsMun, 4), ["vIBSMun"] = N(r.ValorIbsMun) },
                    ["vIBS"] = N(r.ValorIbs),
                    ["gCBS"] = new JsonObject { ["pCBS"] = N(r.AliquotaCbs, 4), ["vCBS"] = N(r.ValorCbs) }
                }
            };
        }

        /// <summary>
        /// Totais = soma exata do que foi enviado em cada det (rejeição 531/533: vBC, vICMS, vProd,
        /// vPIS, vCOFINS do total diferentes do somatório dos itens). Simples/MEI: ICMSTot.vBC/vICMS
        /// zerados — o destaque vai só via CSOSN no item.
        /// </summary>
        private static JsonObject MontarTotal(NotaFiscalModel nota, IReadOnlyList<ItemEmissao> itens)
        {
            decimal vBcTot = itens.Sum(i => i.VBcIcms);
            decimal vIcmsTot = itens.Sum(i => i.VIcms);
            decimal vProd = itens.Sum(i => i.Item.ValorTotal);
            decimal vPis = itens.Sum(i => i.VPis);
            decimal vCofins = itens.Sum(i => i.VCofins);
            decimal vNf = vProd + nota.ValorFrete - nota.ValorDesconto;

            var icmsTot = new JsonObject
            {
                ["vBC"] = N(vBcTot),
                ["vICMS"] = N(vIcmsTot),
                ["vICMSDeson"] = N(0m),
                ["vFCP"] = N(0m),
                ["vBCST"] = N(0m),
                ["vST"] = N(0m),
                ["vFCPST"] = N(0m),
                ["vFCPSTRet"] = N(0m),
                ["vProd"] = N(vProd),
                ["vFrete"] = N(nota.ValorFrete),
                ["vSeg"] = N(0m),
                ["vDesc"] = N(nota.ValorDesconto),
                ["vII"] = N(0m),
                ["vIPI"] = N(0m),
                ["vIPIDevol"] = N(0m),
                ["vPIS"] = N(vPis),
                ["vCOFINS"] = N(vCofins),
                ["vOutro"] = N(0m),
                ["vNF"] = N(vNf)
            };

            var ibsCbsTot = new JsonObject
            {
                ["vBCIBSCBS"] = N(itens.Sum(i => i.IbsCbs.BaseCalculo)),
                ["gIBS"] = new JsonObject
                {
                    ["gIBSUF"] = new JsonObject { ["vDif"] = N(0m), ["vDevTrib"] = N(0m), ["vIBSUF"] = N(itens.Sum(i => i.IbsCbs.ValorIbsUf)) },
                    ["gIBSMun"] = new JsonObject { ["vDif"] = N(0m), ["vDevTrib"] = N(0m), ["vIBSMun"] = N(itens.Sum(i => i.IbsCbs.ValorIbsMun)) },
                    ["vIBS"] = N(itens.Sum(i => i.IbsCbs.ValorIbs)),
                    ["vCredPres"] = N(0m),
                    ["vCredPresCondSus"] = N(0m)
                },
                ["gCBS"] = new JsonObject
                {
                    ["vDif"] = N(0m),
                    ["vDevTrib"] = N(0m),
                    ["vCBS"] = N(itens.Sum(i => i.IbsCbs.ValorCbs)),
                    ["vCredPres"] = N(0m),
                    ["vCredPresCondSus"] = N(0m)
                }
            };

            return new JsonObject { ["ICMSTot"] = icmsTot, ["IBSCBSTot"] = ibsCbsTot };
        }

        private static JsonObject MontarPag(NotaFiscalModel nota)
        {
            decimal vPag = nota.ValorTotalNota;
            string tPag = string.IsNullOrWhiteSpace(nota.FormaPagamento) ? "01" : nota.FormaPagamento.Trim();

            var detPag = new JsonObject
            {
                ["indPag"] = "0",
                ["tPag"] = tPag,
                ["vPag"] = N(vPag)
            };

            // NT 2024/2025: cartão (03/04) e PIX dinâmico (17) exigem grupo card (rejeição 391).
            // Sem TEF: tpIntegra=2 (pagamento não integrado ao sistema).
            if (tPag is "03" or "04" or "17")
            {
                detPag["card"] = new JsonObject
                {
                    ["tpIntegra"] = "2"
                };
            }

            return new JsonObject { ["detPag"] = new JsonArray { detPag } };
        }

        private static JsonValue N(decimal v, int casas = 2) => JsonValue.Create(Math.Round(v, casas));

        private static string SomenteDigitos(string? s) =>
            string.IsNullOrWhiteSpace(s) ? "" : new string(Array.FindAll(s.ToCharArray(), char.IsDigit));

        private static string SomenteDigitosOuIsento(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            if (string.Equals(s.Trim(), "ISENTO", StringComparison.OrdinalIgnoreCase)) return "ISENTO";
            return SomenteDigitos(s);
        }

               /// <summary>Código IBGE da UF (cUF) — reaproveitado pela tela de inutilização de numeração.</summary>
               public static string UfToCodigo(string? uf) => (uf ?? "").Trim().ToUpperInvariant() switch
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
