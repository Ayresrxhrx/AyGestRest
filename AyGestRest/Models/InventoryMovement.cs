using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AyGestRest.Models
{
    public class InventoryMovement
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public Product Product { get; set; }
        public string MovementType { get; set; } // "Entrada" ou "Saída"
        public int Quantity { get; set; }
        public decimal PreviousStock { get; set; }
        public decimal NewStock { get; set; }
        public string Reason { get; set; }
        public string Notes { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
