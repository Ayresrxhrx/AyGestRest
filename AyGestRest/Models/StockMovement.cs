using System;

namespace AyGestRest.Models
{
    public class StockMovement
    {
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product Product { get; set; }

        public DateTime Date { get; set; }
        public string Type { get; set; } // Entrada, Saída, Ajuste, Perda
        public int Quantity { get; set; }
        public decimal BalanceAfter { get; set; }
    }
}
