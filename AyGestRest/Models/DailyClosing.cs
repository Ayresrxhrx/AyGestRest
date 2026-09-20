
using System.ComponentModel.DataAnnotations;

namespace AyGestRest.Models
{
    public class DailyClosing

    {
        [Key] // <-- chave primária obrigatória
        public int Id { get; set; }

        public DateTime Data { get; set; }
        public decimal TotalVendido { get; set; }
        public decimal TotalDinheiro { get; set; }
        public decimal TotalOutrosPagamentos { get; set; }
        public decimal LucroBrutoEstimado { get; set; }
        public decimal ValorContado { get; set; }
        public decimal Diferenca { get; set; }
        public bool IsCurrent { get; set; } = true;
    }
}