using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AyGestRest.Models
{
    public class HistoricoEstoqueDiario
    {
        public int Id { get; set; }
        public DateTime Data { get; set; }
        public int ProductId { get; set; }
        public Product Product { get; set; }
        public decimal QuantidadeInicial { get; set; }
        public decimal QuantidadeFinal { get; set; }
        public decimal VendasDia { get; set; }
        public decimal ComprasDia { get; set; }
        public decimal AjustesDia { get; set; }
        public DateTime DataRegistro { get; set; } = DateTime.Now;
    }
}
