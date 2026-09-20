using System.Collections.Generic;

namespace AyGestRest.Models
{
    public class Product
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Code { get; set; } = string.Empty;

        public string? Description { get; set; }

        public decimal Price { get; set; }

        public bool Active { get; set; } = true;

        // Stock do produto (para produtos simples). Para produtos compostos, o stock é calculado a partir dos ingredientes.
        public decimal Stock { get; set; } = 0;

        public string Unit { get; set; } = "unidade";

        // Categoria
        public int CategoryId { get; set; }
        public ProductCategory? Category { get; set; }

        // Indica se o produto é composto (usa receita/ingredientes)
        public bool IsComposite { get; set; } = false;
        public bool PrintToKitchen { get; set; } = false;
        // Relação muitos-para-muitos com ingredientes via tabela de ligação
        public ICollection<ProductIngredient> ProductIngredients { get; set; } = new List<ProductIngredient>();

        // Código de barras (para leitor)
        public string? Barcode { get; set; } = string.Empty;

        // Destaque na tela do POS
        public bool Featured { get; set; } = false;

        // Produto em promoção
        public bool Promo { get; set; } = false;

        // Preço promocional (se aplicável) - opcional, pode adicionar se quiser usar
        // public decimal? PromoPrice { get; set; }

        // Imagem do produto
        public byte[]? Image { get; set; }
        public bool TrackInventory { get; internal set; }
    }
}