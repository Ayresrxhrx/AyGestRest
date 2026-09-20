namespace AyGestRest.Models
{
    public class Country
    {
        public string Code { get; set; } = ""; // +258
        public string Name { get; set; } = ""; // Moçambique
        public string Flag { get; set; } = ""; // 🇲🇿
        public string PhoneMask { get; set; } = ""; // Máscara para validação public string DisplayText => $"{Flag} {Name} (+{PhoneCode})";
    }
}