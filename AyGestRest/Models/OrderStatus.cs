using System;
using System.Collections.Generic;
using System.Text;

namespace AyGestRest.Models
{
    public enum OrderStatus
    {
        Aberto = 0,
        EmPreparacao = 1,
        Pronto = 2,
        Pago = 3,
        Cancelado = 4,
        Fechado = 5
    }
}
