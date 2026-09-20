using System;

namespace AyGestRest.Models
{
    public class Payment
    {
        public int Id { get; set; }

        public int OrderId { get; set; }
        public Order? Order { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.Now;
        public DateTime Data { get; set; } = DateTime.Now;

        public decimal Amount { get; set; }
        public decimal? Valor { get; set; }

        public PaymentType Type { get; set; }
        public string TipoPagamento { get; set; } = string.Empty;

        public string Reference { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public decimal Cash { get; internal set; }
        public decimal Card { get; internal set; }
        public decimal MBWay { get; internal set; }
        public decimal TotalPaid { get; internal set; }
        public DateTime CreatedAt { get; internal set; }
    }
}
