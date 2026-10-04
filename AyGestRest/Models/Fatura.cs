using System;
using System.Collections.Generic;

namespace AyGestRest.Models
{
    public enum FaturaEstado
    {
        Emitida = 1,
        Anulada = 2,
        EmitidaComCredito = 3
    }

    public class Fatura
    {
        public int Id { get; set; }
        public string Numero { get; set; } = string.Empty;
        public string Serie { get; set; } = "A";
        public DateTime DataEmissao { get; set; } = DateTime.Now;
        public int? OrderId { get; set; }
        public Order? Order { get; set; }
        public int? ClienteId { get; set; }
        public Cliente? Cliente { get; set; }
        public string ClienteNome { get; set; } = string.Empty;
        public string ClienteNuit { get; set; } = string.Empty;
        public string Moeda { get; set; } = "MZN";
        public decimal Subtotal { get; set; }
        public decimal Desconto { get; set; }
        public decimal Imposto { get; set; }
        public decimal Total { get; set; }
        public decimal ValorPago { get; set; }
        public decimal Troco { get; set; }
        public string MetodoPagamento { get; set; } = string.Empty;
        public string NUITEmitente { get; set; } = string.Empty;
        public string NomeEmitente { get; set; } = string.Empty;
        public FaturaEstado Estado { get; set; } = FaturaEstado.Emitida;
        public string Observacoes { get; set; } = string.Empty;
        public string TerminalId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? AnuladaEm { get; set; }
        public string MotivoAnulacao { get; set; } = string.Empty;

        public ICollection<FaturaItem> Itens { get; set; } = new List<FaturaItem>();
    }
}
