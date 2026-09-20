using System.Collections.Generic;

namespace AyGestRest.Models
{
    public sealed class MobileOrderRequest
    {
        public int? TableId { get; set; }
        public int? UserId { get; set; }
        public string? DeviceId { get; set; }
        public string? Observations { get; set; }
        public List<MobileOrderItemRequest> Items { get; set; } = new();
    }

    public sealed class MobileOrderItemRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public string? Notes { get; set; }
    }
}
