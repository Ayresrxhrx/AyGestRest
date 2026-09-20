using System;
using System.Collections.Generic;

namespace AyGestRest.Models
{
    public class Ingredient
    {
        public int Id { get; set; }

        // Nome do ingrediente
        public string Name { get; set; } = string.Empty;

        // Quantidade em stock
        public int Quantity { get; set; }

        public decimal Cost { get; set; }   // 👈 CUSTO UNITÁRIO REAL
        public decimal Stock { get; set; }
        public ICollection<ProductIngredient> ProductIngredients { get; set; } = new List<ProductIngredient>();

        // Unidade de medida, ex: "kg", "unidade", "g"
        public string Unit { get; set; } = "unidade";

        // Se é um ingrediente crítico (alerta de stock baixo)
        public bool Critical { get; set; } = false;

        // Relacionamento com produtos
        public ICollection<Product> Products { get; set; } = new List<Product>();
    }
}
