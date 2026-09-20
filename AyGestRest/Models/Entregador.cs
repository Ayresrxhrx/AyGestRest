using System;
using System.Collections.Generic;
using System.Text;

namespace AyGestRest.Models
{
    public class Entregador
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string Telefone { get; set; }
        public bool Ativo { get; set; } = true;
    }

}
