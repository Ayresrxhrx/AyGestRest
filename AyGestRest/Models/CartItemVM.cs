namespace AyGestRest.Models
{
    public class CartItemVM
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }

        public decimal SubTotal => UnitPrice * Quantity;
    }
}
