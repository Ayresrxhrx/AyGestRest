namespace AyGestRest.Models
{
    public class FaturaItem
    {
        public int Id { get; set; }
        public int FaturaId { get; set; }
        public Fatura? Fatura { get; set; }
        public int? ProductId { get; set; }
        public Product? Product { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public decimal Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal Desconto { get; set; }
        public decimal TaxaIVA { get; set; }
        public decimal ValorIVA { get; set; }
        public decimal Total { get; set; }
    }
}
