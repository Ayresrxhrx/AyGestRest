using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AyGestRest.Models
{
    public class OrderItem
    {
        public int Id { get; set; }

        public int OrderId { get; set; }
        public Order? Order { get; set; }

        public int ProductId { get; set; }
        public Product? Product { get; set; }

        public int Quantity { get; set; } = 1;
        public decimal PriceAtMoment { get; set; } // preço no momento da venda

        public string Notes { get; set; } = string.Empty;

        public KitchenStatus KitchenStatus { get; set; } = KitchenStatus.Pendente;
        public string Estado { get; set; } = "Pendente";

        public string Name { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Remover internal set e usar public
        public decimal UnitPrice { get; set; }

        public decimal Total { get; set; }

        // 🔥 NOVAS PROPRIEDADES PARA RELATÓRIOS
        public string PromotionsApplied { get; set; } = string.Empty; // Promoções aplicadas ao item

        // 🔥 PROPRIEDADES CALCULADAS - NÃO MAPEAR PARA O BANCO
        [NotMapped]
        public decimal TotalItem => Quantity * PriceAtMoment;         // Total calculado do item

        [NotMapped]
        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : Product?.Name ?? "—";

        [NotMapped]
        public string ProductCategory => Product?.Category?.Nome ?? "—";
    }
}