using System.Collections.Generic;

namespace AyGestRest.Models
{
    public class ProductCategory
    {
        public int Id { get; set; }

        // Nome da categoria
        public string Nome { get; set; } = string.Empty;

        // Cor da categoria (hexadecimal)
        public string CorHex { get; set; } = "#FFFFFF";

        // Ativo/inativo
        public bool Ativo { get; set; } = true;

        // Ícone opcional
        public string Icone { get; set; } = string.Empty;

        // Relacionamento com produtos (opcional)
        public ICollection<Product> Products { get; set; } = new List<Product>();
    }
}
