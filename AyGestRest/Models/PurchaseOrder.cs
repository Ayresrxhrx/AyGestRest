using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AyGestRest.Models
{
    public class PurchaseOrder
    {
        public int Id { get; set; }
        public string PurchaseNumber { get; set; }
        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; }
        public string PurchaseType { get; set; } // "Compra", "Requisição", "Pedido"
        public string Status { get; set; } // "Pendente", "Recebida", "Cancelada"
        public decimal TotalAmount { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }

        public DateTime? ReceivedAt { get; set; }

        // Propriedade de navegação
        public ICollection<PurchaseOrderItem> Items { get; set; }

        // Propriedade calculada (não mapeada)
        [NotMapped]
        public int ItemCount => Items?.Count ?? 0;
    }
}
