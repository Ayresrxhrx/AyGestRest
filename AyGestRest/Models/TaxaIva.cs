using System;
using System.Collections.Generic;
using System.Text;

namespace AyGestRest.Models
{
    public class TaxaIVA
    {
        public int Id { get; set; }
        public string Descricao { get; set; } = "";
        public double Valor { get; set; }
        public string Tipo { get; set; } = "Normal";
        public bool Ativa { get; set; } = true;
    }
}
