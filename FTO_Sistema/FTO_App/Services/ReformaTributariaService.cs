using System;
using FTO_App.Models;

namespace FTO_App.Services
{
    /// <summary>
    /// Reforma tributária (EC 132/2023 + LC 214/2025 arts. 343/346/348):
    /// CBS (federal) e IBS (estadual/municipal — IVA dual).
    /// 2026 = alíquota-teste CBS 0,9% + IBS 0,1% na UF (pIBSMun=0) — NT 2025.002 / rejeição 1026.
    /// </summary>
    public static class ReformaTributariaService
    {
        public const string PresetTeste2026 = "teste2026";
        public const string PresetProjetadoCheio = "projetado";
        public const string PresetPersonalizado = "personalizado";

        /// <summary>CST padrão tributação integral (destaque obrigatório em DF-e regime regular).</summary>
        public const string CstPadrao = "000";
        /// <summary>cClassTrib padrão operação tributada integralmente (TcClassTrib = 6 dígitos).</summary>
        public const string ClassTribPadrao = "000001";

        public sealed class Resultado
        {
            public decimal BaseCalculo { get; init; }
            public decimal AliquotaCbs { get; init; }
            public decimal AliquotaIbs { get; init; }
            public decimal AliquotaIbsUf { get; init; }
            public decimal AliquotaIbsMun { get; init; }
            public decimal ValorCbs { get; init; }
            public decimal ValorIbs { get; init; }
            public decimal ValorIbsUf { get; init; }
            public decimal ValorIbsMun { get; init; }
            public decimal ValorTotalIva { get; init; }
            public string Cst { get; init; } = CstPadrao;
            public string ClassTrib { get; init; } = ClassTribPadrao;
            public string Observacao { get; init; } = "";
        }

        /// <summary>
        /// Referência de CBS cheia usada enquanto a alíquota definitiva não é fixada por resolução
        /// do Senado. Serve só de fallback: o valor real vem de Configurações → IBS / CBS.
        /// </summary>
        public const decimal CbsReferenciaProjetada = 9.21m;

        /// <summary>
        /// Alíquotas oficiais de transição por ano de emissão (LC 214/2025 arts. 343/346, NT 2025.002).
        ///
        /// • 2025-2026 — fase de teste: CBS 0,9% e pIBSUF=0,1% com pIBSMun=0.
        ///   NÃO dividir o IBS em 0,05/0,05 aqui (gera rejeição 1026).
        /// • 2027-2028 — CBS entra em alíquota cheia (reduzida em 0,1 p.p.) e o IBS fica em
        ///   0,05% estadual + 0,05% municipal.
        /// • 2029+ — transição do IBS; as alíquotas passam a vir da configuração.
        ///
        /// A CBS cheia depende de resolução do Senado: é lida de Configurações → IBS / CBS
        /// (presets "Projetado cheio" ou "Personalizado") e só cai no fallback projetado se
        /// a configuração ainda estiver no preset de teste.
        /// </summary>
        public static (decimal cbs, decimal ibsUf, decimal ibsMun) AliquotasOficiaisTransicao(
            int anoEmissao, EmpresaConfig? cfg = null)
        {
            if (anoEmissao <= 2026) return (0.9m, 0.1m, 0m);

            decimal cbsCheia = CbsCheiaConfigurada(cfg);
            if (anoEmissao is 2027 or 2028)
                return (Math.Max(0m, cbsCheia - 0.1m), 0.05m, 0.05m);

            var (_, _, ufCfg, munCfg) = AliquotasDoPreset(null, cfg);
            return (cbsCheia, ufCfg, munCfg);
        }

        /// <summary>
        /// CBS cheia configurada pelo usuário. O preset de teste (0,9%) é ignorado de propósito:
        /// usar a alíquota-teste depois de 2026 emitiria imposto a menos.
        /// </summary>
        public static decimal CbsCheiaConfigurada(EmpresaConfig? cfg = null)
        {
            cfg ??= EmpresaConfigStore.Current;
            bool presetDeTeste = string.IsNullOrWhiteSpace(cfg.IbsCbsPreset) ||
                                 cfg.IbsCbsPreset == PresetTeste2026;
            if (!presetDeTeste && cfg.CbsAliquota > 1m) return cfg.CbsAliquota;
            return CbsReferenciaProjetada;
        }

        /// <summary>True quando a CBS cheia ainda não foi confirmada em Configurações.</summary>
        public static bool UsandoCbsDeFallback(EmpresaConfig? cfg = null)
        {
            cfg ??= EmpresaConfigStore.Current;
            bool presetDeTeste = string.IsNullOrWhiteSpace(cfg.IbsCbsPreset) ||
                                 cfg.IbsCbsPreset == PresetTeste2026;
            return presetDeTeste || cfg.CbsAliquota <= 1m;
        }

        public static (decimal cbs, decimal ibs, decimal ibsUf, decimal ibsMun) AliquotasDoPreset(string? preset, EmpresaConfig? cfg = null)
        {
            cfg ??= EmpresaConfigStore.Current;
            return (preset ?? cfg.IbsCbsPreset) switch
            {
                PresetProjetadoCheio => (9.21m, 18.70m, 9.35m, 9.35m),
                PresetPersonalizado => (
                    cfg.CbsAliquota,
                    cfg.IbsAliquota,
                    cfg.IbsAliquotaUf > 0 ? cfg.IbsAliquotaUf : cfg.IbsAliquota,
                    cfg.IbsAliquotaMun),
                // Teste 2026: IBS inteiro na UF (0,1%); município zerado — art. 343 LC 214/2025
                _ => (0.9m, 0.1m, 0.1m, 0m)
            };
        }

        public static string DescricaoPreset(string? preset) => (preset ?? PresetTeste2026) switch
        {
            PresetProjetadoCheio => "Projetado cheio (~27,91%: CBS 9,21% + IBS 18,7%)",
            PresetPersonalizado => "Personalizado (Configurações)",
            _ => "Teste 2026 (CBS 0,9% + IBS UF 0,1% — SEFAZ exige pIBSUF=0,1)"
        };

        /// <summary>CST IBS/CBS: exatamente 3 dígitos (ex.: 000).</summary>
        public static string NormalizarCst(string? cst)
        {
            string d = SomenteDigitos(cst);
            if (d.Length == 3) return d;
            if (d.Length > 3) return d[^3..];
            if (d.Length > 0) return d.PadLeft(3, '0');
            return CstPadrao;
        }

        /// <summary>
        /// cClassTrib (TcClassTrib): exatamente 6 dígitos. Valores como "0" ou vazios
        /// falham no XSD (pattern) — rejeição local XSD_VALIDATION.
        /// </summary>
        public static string NormalizarClassTrib(string? classTrib)
        {
            string d = SomenteDigitos(classTrib);
            return d.Length == 6 ? d : ClassTribPadrao;
        }

        /// <summary>NCM no XML/JSON: só dígitos; válido com 2 (capítulo) ou 8 (completo).</summary>
        public static string NormalizarNcm(string? ncm) => SomenteDigitos(ncm);

        public static bool NcmValido(string? ncm)
        {
            string d = SomenteDigitos(ncm);
            return d.Length is 2 or 8;
        }

        /// <summary>
        /// Calcula IBS/CBS sobre a base (valor do item/produtos).
        /// Redução percentual aplica-se às alíquotas (ex.: 60% = cobra 40% da alíquota).
        /// </summary>
        public static Resultado Calcular(
            decimal baseCalculo,
            EmpresaConfig? cfg = null,
            decimal? aliqCbsOverride = null,
            decimal? aliqIbsOverride = null,
            decimal reducaoPercentual = 0,
            string? cst = null,
            string? classTrib = null)
        {
            cfg ??= EmpresaConfigStore.Current;
            var (cbs, ibs, ibsUf, ibsMun) = AliquotasDoPreset(cfg.IbsCbsPreset, cfg);

            if (aliqCbsOverride.HasValue) cbs = aliqCbsOverride.Value;
            if (aliqIbsOverride.HasValue)
            {
                ibs = aliqIbsOverride.Value;
                // Na fase teste o IBS vai integralmente para a UF
                if (cfg.IbsCbsPreset == PresetTeste2026 || string.IsNullOrWhiteSpace(cfg.IbsCbsPreset))
                {
                    ibsUf = ibs;
                    ibsMun = 0m;
                }
                else
                {
                    ibsUf = ibs / 2m;
                    ibsMun = ibs / 2m;
                }
            }

            if (reducaoPercentual > 0 && reducaoPercentual <= 100)
            {
                decimal fator = 1m - (reducaoPercentual / 100m);
                cbs *= fator;
                ibs *= fator;
                ibsUf *= fator;
                ibsMun *= fator;
            }

            baseCalculo = Math.Round(Math.Max(0, baseCalculo), 2);
            decimal vCbs = Math.Round(baseCalculo * cbs / 100m, 2);
            decimal vIbs = Math.Round(baseCalculo * ibs / 100m, 2);
            decimal vUf = Math.Round(baseCalculo * ibsUf / 100m, 2);
            decimal vMun = Math.Round(baseCalculo * ibsMun / 100m, 2);
            // Ajuste centavos: soma UF+Mun deve fechar o IBS
            if (vUf + vMun != vIbs)
                vMun = vIbs - vUf;

            string obs = cfg.IbsCbsPreset == PresetTeste2026
                ? "Valores de teste 2026: pIBSUF=0,1% e pIBSMun=0 (NT 2025.002 / rejeição 1026)."
                : "Cálculo IBS/CBS conforme alíquotas configuradas.";

            if (string.Equals(cfg.RegimeTributario, "1", StringComparison.Ordinal) &&
                cfg.IbsCbsPreset == PresetTeste2026)
            {
                obs += " Simples Nacional: destaque obrigatório de IBS/CBS em DF-e a partir de 2027 (opcional em 2026).";
            }

            return new Resultado
            {
                BaseCalculo = baseCalculo,
                AliquotaCbs = cbs,
                AliquotaIbs = ibs,
                AliquotaIbsUf = ibsUf,
                AliquotaIbsMun = ibsMun,
                ValorCbs = vCbs,
                ValorIbs = vIbs,
                ValorIbsUf = vUf,
                ValorIbsMun = vMun,
                ValorTotalIva = vCbs + vIbs,
                Cst = NormalizarCst(cst),
                ClassTrib = NormalizarClassTrib(classTrib),
                Observacao = obs
            };
        }

        /// <summary>
        /// Recalcula IBS/CBS para emissão na SEFAZ no ano corrente, forçando as alíquotas
        /// oficiais de transição (evita rejeição 1026 mesmo se o rascunho tiver 0,05/0,05).
        /// Preset "projetado" só vale para simulação local — na emissão em 2025/2026 a SEFAZ
        /// exige os valores de transição.
        /// </summary>
        public static Resultado CalcularParaEmissao(
            decimal baseCalculo, NotaFiscalModel nota, int? anoEmissao = null, EmpresaConfig? cfg = null) =>
            CalcularParaEmissao(baseCalculo, nota.CbsAliquota, nota.IbsAliquotaUf, nota.IbsAliquotaMun,
                nota.CstIbsCbs, nota.ClassTrib, anoEmissao, cfg);

        /// <summary>Mesmo cálculo de <see cref="CalcularParaEmissao(decimal, NotaFiscalModel, int?, EmpresaConfig?)"/>,
        /// por item da NF-e — cada det leva o seu grupo IBSCBS.</summary>
        public static Resultado CalcularParaEmissao(
            NotaFiscalItemModel item, int? anoEmissao = null, EmpresaConfig? cfg = null) =>
            CalcularParaEmissao(item.ValorTotal, item.CbsAliquota, item.IbsAliquotaUf, item.IbsAliquotaMun,
                item.CstIbsCbs, item.ClassTrib, anoEmissao, cfg);

        private static Resultado CalcularParaEmissao(
            decimal baseCalculo, decimal cbsInformada, decimal ibsUfInformada, decimal ibsMunInformada,
            string? cst, string? classTrib, int? anoEmissao, EmpresaConfig? cfg)
        {
            int ano = anoEmissao ?? DateTime.Now.Year;
            var (cbsOficial, ibsUfOficial, ibsMunOficial) = AliquotasOficiaisTransicao(ano, cfg);

            // Em 2025-2028 usa alíquotas oficiais de transição; depois respeita a nota/config
            decimal cbs = ano <= 2028 ? cbsOficial : (cbsInformada > 0 ? cbsInformada : cbsOficial);
            decimal ibsUf = ano <= 2028 ? ibsUfOficial : ibsUfInformada;
            decimal ibsMun = ano <= 2028 ? ibsMunOficial : ibsMunInformada;
            decimal ibs = ibsUf + ibsMun;

            baseCalculo = Math.Round(Math.Max(0, baseCalculo), 2);
            decimal vCbs = Math.Round(baseCalculo * cbs / 100m, 2);
            decimal vUf = Math.Round(baseCalculo * ibsUf / 100m, 2);
            decimal vMun = Math.Round(baseCalculo * ibsMun / 100m, 2);
            decimal vIbs = vUf + vMun;

            return new Resultado
            {
                BaseCalculo = baseCalculo,
                AliquotaCbs = cbs,
                AliquotaIbs = ibs,
                AliquotaIbsUf = ibsUf,
                AliquotaIbsMun = ibsMun,
                ValorCbs = vCbs,
                ValorIbs = vIbs,
                ValorIbsUf = vUf,
                ValorIbsMun = vMun,
                ValorTotalIva = vCbs + vIbs,
                Cst = NormalizarCst(cst),
                ClassTrib = NormalizarClassTrib(classTrib),
                Observacao =
                    $"Emissão {ano}: pIBSUF={ibsUf:0.####}% / pIBSMun={ibsMun:0.####}% / pCBS={cbs:0.####}% (LC 214/2025)." +
                    (ano >= 2027 && UsandoCbsDeFallback(cfg)
                        ? " ⚠ CBS usando a referência projetada — confirme a alíquota cheia em Configurações → IBS / CBS."
                        : "")
            };
        }

        public static Resultado CalcularParaProduto(decimal baseCalculo, ProdutoModel? produto, EmpresaConfig? cfg = null)
        {
            if (produto == null)
                return Calcular(baseCalculo, cfg);

            decimal? oCbs = produto.CbsAliquota > 0 ? produto.CbsAliquota : null;
            decimal? oIbs = produto.IbsAliquota > 0 ? produto.IbsAliquota : null;
            return Calcular(
                baseCalculo,
                cfg,
                oCbs,
                oIbs,
                produto.IbsCbsReducao,
                produto.CstIbsCbs,
                produto.ClassTrib);
        }

        private static string SomenteDigitos(string? s) =>
            string.IsNullOrWhiteSpace(s) ? "" : new string(Array.FindAll(s.ToCharArray(), char.IsDigit));
    }
}
