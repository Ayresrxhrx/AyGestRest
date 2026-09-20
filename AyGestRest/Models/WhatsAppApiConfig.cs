// WhatsAppApiConfig.cs
using System.ComponentModel.DataAnnotations;

namespace AyGestRest.Models
{
    public class WhatsAppApiConfig
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string AccessToken { get; set; } = "";

        [Required]
        public string PhoneNumberId { get; set; } = "";

        public string BusinessAccountId { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public bool IsConfigured => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(PhoneNumberId);
    }
}