using AyGestRest.Models;

public class Order
{
    public int Id { get; set; }

    public int? TableId { get; set; }
    public RestaurantTable? Table { get; set; }

    public DateTime OpenDate { get; set; } = DateTime.Now;
    public DateTime? CloseDate { get; set; }
    public int? ReservationId { get; set; } // Nova propriedade
    public Reservation? Reservation { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Aberto;

    public decimal Total { get; set; }
    public string Observations { get; set; } = string.Empty;

    public DateTime Data { get; set; } = DateTime.Now;

    public int UserId { get; set; }
    public User? User { get; set; }

    public Entregador? Entregador { get; set; }
    public int? EntregadorId { get; set; }
    public List<OrderItem> Items { get; set; } = new();

    // DELIVERY
    public string Estado { get; set; } = string.Empty;
    public string ClienteNome { get; set; } = string.Empty;
    public string Morada { get; set; } = string.Empty;
    public string Telefone { get; set; } = string.Empty;
    public string Motoboy { get; set; } = string.Empty;

    public DateTime DataPedido { get; set; } = DateTime.Now;
    public int TempoEstimado { get; set; }
    public decimal TaxaEntrega { get; set; }
    public bool Fecha { get; set; }

    public List<Payment> Payments { get; set; } = new();

    // 🔥 PROPRIEDADES PARA UI
    public string Mesa => Table != null ? Table.Number : "—";
    public string Utilizador => User != null ? User.FullName : "—";

    // Corrigido: agora dá pra atribuir
    public decimal TotalAmount { get; set; }        // antes era internal set
    public bool IsPaid { get; set; }               // antes era internal set

    public DateTime PaidAt { get; set; }           // antes não tinha set público
    public DateTime CreatedAt { get; internal set; }
    public int TalaoNumber { get; set; }

    public DateTime ClosedAt { get; internal set; }
    public bool IsClosed { get; internal set; }
}
