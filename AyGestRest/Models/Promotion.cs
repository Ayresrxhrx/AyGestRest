using System;
using System.Collections.Generic;

namespace AyGestRest.Models
{
    public class Promotion
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";           // 2x1, Desconto %, Happy Hour, Cupão
        public string DiscountValue { get; set; } = ""; // Ex: "10%", "Leve 2 pague 1"
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; } = true;

        // Relação com produtos (opcional)
        public List<Product> Produtos { get; set; } = new();
    }
}
