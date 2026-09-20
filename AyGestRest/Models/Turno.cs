using System;

namespace AyGestRest.Models
{
    public class Turno
    {
        public int Id { get; set; }
        public int FuncionarioId { get; set; }
        public DateTime Data { get; set; }
        public TimeSpan? HoraInicio { get; set; }
        public TimeSpan HoraFim { get; set; }
        public string? Observacoes { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public string Estado { get; set; } = "Agendado";
        public string? CriadoPor { get; set; }
        public DateTime? CriadoEm { get; set; }
        public DateTime? AtualizadoEm { get; set; }

        // Propriedades auxiliares
        public string? TipoTurno { get; set; }
        public bool? RepetirSemanalmente { get; set; }
        public bool? NotificarFuncionario { get; set; }
    }
}