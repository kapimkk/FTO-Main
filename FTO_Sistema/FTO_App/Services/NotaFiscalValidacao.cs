using System.Collections.Generic;
using System.Linq;
using FTO_App.Models;

namespace FTO_App.Services
{
    /// <summary>
    /// Regras mínimas de um item de NF-e antes de salvar ou transmitir. Uma lista única, usada pelo
    /// editor de item, pelo Salvar da nota e pela janela de ações fiscais — para a tela não aceitar
    /// algo que a emissão vai recusar depois.
    /// </summary>
    public static class NotaFiscalValidacao
    {
        /// <summary>Problemas de um item isolado (sem prefixo de posição).</summary>
        public static List<string> ValidarItem(NotaFiscalItemModel item)
        {
            var erros = new List<string>();

            if (string.IsNullOrWhiteSpace(item.Descricao))
                erros.Add("descrição vazia");
            if (!ReformaTributariaService.NcmValido(item.Ncm))
                erros.Add("NCM inválido (8 dígitos)");
            if (DocumentValidator.OnlyDigits(item.Cfop).Length != 4)
                erros.Add("CFOP inválido (4 dígitos)");
            if (string.IsNullOrWhiteSpace(item.Unidade))
                erros.Add("unidade vazia");
            if (item.Quantidade <= 0)
                erros.Add("quantidade deve ser maior que zero");
            if (item.ValorUnitario <= 0)
                erros.Add("valor unitário deve ser maior que zero");

            return erros;
        }

        /// <summary>Problemas de todos os itens, já com "Item N (descrição)" na frente.</summary>
        public static List<string> ValidarItens(IReadOnlyList<NotaFiscalItemModel> itens)
        {
            var erros = new List<string>();
            if (itens.Count == 0)
            {
                erros.Add("a nota precisa de pelo menos um item");
                return erros;
            }

            for (int i = 0; i < itens.Count; i++)
            {
                var problemas = ValidarItem(itens[i]);
                if (problemas.Count == 0) continue;

                string nome = string.IsNullOrWhiteSpace(itens[i].Descricao) ? "" : $" ({Encurtar(itens[i].Descricao, 30)})";
                erros.Add($"Item {i + 1}{nome}: {string.Join(", ", problemas)}");
            }

            // A SEFAZ limita a NF-e a 990 itens (nItem de 1 a 990).
            if (itens.Count > 990)
                erros.Add($"a NF-e aceita no máximo 990 itens (há {itens.Count})");

            return erros;
        }

        private static string Encurtar(string texto, int max)
        {
            texto = texto.Trim();
            return texto.Length <= max ? texto : texto[..(max - 1)] + "…";
        }
    }
}
