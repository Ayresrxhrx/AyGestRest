using System;
using System.Collections.Generic;

namespace AyGestRest.Models
{
    public class RestaurantTable
    {
        public int Id { get; set; }

        public string Number { get; set; } = string.Empty; // "Mesa 1"
        public string Name { get; set; } = string.Empty;

        public int Capacity { get; set; }

        public TableStatus Status { get; set; } = TableStatus.Livre;

        public double PositionX { get; set; }
        public double PositionY { get; set; }

        public bool Selecionado { get; set; } = false;

        public string Notes { get; set; } = string.Empty;

        public virtual ICollection<Reservation> Reservations { get; set; }
            = new List<Reservation>();
    }
}
