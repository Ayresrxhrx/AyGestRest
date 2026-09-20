using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AyGestRest.Models
{
    public enum ReservationStatus
    {
        Pendente,
        Confirmada,
        Cancelada,
        Concluida,
        Ativa
    }

    public class Reservation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string NomeCliente { get; set; }

        [Required]
        public int ClienteId { get; set; }

        [ForeignKey("ClienteId")]
        public Cliente Cliente { get; set; }

        [Required]
        public int RestaurantTableId { get; set; }

        [ForeignKey("RestaurantTableId")]
        public RestaurantTable RestaurantTable { get; set; }

        [Required]
        public DateTime Inicio { get; set; }

        [Required]
        public DateTime Fim { get; set; }

        [Required]
        [Range(1, 50)]
        public int NumPessoas { get; set; }

        [Required]
        public ReservationStatus Estado { get; set; } = ReservationStatus.Confirmada;

        [Required]
        public DateTime DataCriacao { get; set; } = DateTime.Now;

        public DateTime? DataAtualizacao { get; set; }

        [Required]
        [MaxLength(10)]
        public string CodigoVerificacao { get; set; }

        [Required]
        public bool MesaAtiva { get; set; } = false;

        public DateTime? DataAtivacao { get; set; }

        public DateTime? DataDesativacao { get; set; }

        // Propriedades para exibição na UI
        [NotMapped]
        public string EstadoDisplay { get; set; } = string.Empty;

        [NotMapped]
        public bool Atrasada { get; set; } = false;
        public string? Observacoes { get; internal set; }

        public Reservation()
        {
            // Inicializa todas as propriedades para evitar NullReferenceException
            NomeCliente = string.Empty;
            CodigoVerificacao = GerarCodigoVerificacao();
        }

        private string GerarCodigoVerificacao()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}