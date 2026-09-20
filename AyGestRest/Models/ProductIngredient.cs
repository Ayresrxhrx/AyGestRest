using System;
using System.Collections.Generic;
using System.Text;

namespace AyGestRest.Models
{
    public class ProductIngredient
    {
        public int ProductId { get; set; }
        public Product? Product { get; set; }

        public int IngredientId { get; set; }
        public Ingredient? Ingredient { get; set; }

        public int QuantityUsed { get; set; } = 1; // quanto usa por produto
    }
}
