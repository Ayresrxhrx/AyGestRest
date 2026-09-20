using System;

namespace AyGestRest.Models
{
    public class MobileDevice
    {
        public int Id { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int? TableId { get; set; }
        public MobileDeviceMode Mode { get; set; } = MobileDeviceMode.Mesa;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public RestaurantTable? Table { get; set; }
    }

    public enum MobileDeviceMode
    {
        Mesa = 0,
        Garcom = 1
    }
}
