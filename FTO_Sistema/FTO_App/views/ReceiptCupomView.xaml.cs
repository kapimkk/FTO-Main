using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FTO_App.Models;

namespace FTO_App.Views
{
    public partial class ReceiptCupomView : UserControl
    {
        /// <summary>Largura nominal do cupom 80mm em pixels (96 DPI).</summary>
        public const double LarguraCupomPx = 302;

        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        public ReceiptCupomView()
        {
            InitializeComponent();
        }

        public FrameworkElement CupomParaImpressao => BorderCupom;

        public void Inicializar(Venda venda)
        {
            if (venda is null) throw new ArgumentNullException(nameof(venda));

            TxtNumeroVenda.Text = venda.Id.ToString(PtBr);
            TxtData.Text = venda.DataFormatada;
            TxtImpressoEm.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", PtBr);
            TxtServico.Text = string.IsNullOrWhiteSpace(venda.TipoServico) ? "-" : venda.TipoServico.Trim();
            TxtTotal.Text = venda.VendaValor.ToString("C2", PtBr);
            TxtFormaPagamento.Text = string.IsNullOrWhiteSpace(venda.FormaPag) ? "-" : venda.FormaPag.Trim();
        }

        /// <summary>Define largura fixa e mede o layout para impressão sem área vazia lateral.</summary>
        public void PrepararParaImpressao(double larguraPx)
        {
            if (larguraPx <= 0) larguraPx = LarguraCupomPx;

            Width = larguraPx;
            BorderCupom.Width = larguraPx;

            Measure(new Size(larguraPx, double.PositiveInfinity));
            Arrange(new Rect(0, 0, larguraPx, DesiredSize.Height));
            UpdateLayout();
        }
    }
}
