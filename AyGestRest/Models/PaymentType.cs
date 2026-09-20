using System;
using System.Collections.Generic;
using System.Text;

namespace AyGestRest.Models
{
    public enum PaymentType
    {
        Dinheiro = 0,
        Multibanco = 1,
        MBWay = 2,
        CartaoCredito = 3,
        CartaoDebito = 4,
        MPesa = 5,        // Mantém apenas esta
        Emola = 6,
        BCI = 7,
        BIM = 8,
        Ponto24 = 9,
        Outro = 99,
        Cartao = 100,
        PosNedBank = 101,        // REMOVA a linha: Mpesa = 101
        PosMoza = 102
    }
}
