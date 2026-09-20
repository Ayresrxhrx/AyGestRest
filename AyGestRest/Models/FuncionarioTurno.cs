using System;
using System.ComponentModel.DataAnnotations;

namespace AyGestRest.Models
{// Adicione estas propriedades ao seu modelo FuncionarioTurno no contexto do Entity Framework
 // Atualize seu modelo FuncionarioTurno com estas propriedades adicionais
    public class FuncionarioTurno
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime Data { get; set; }
        public TimeSpan? HoraEntrada { get; set; }
        public TimeSpan? HoraSaida { get; set; }
        public TimeSpan? HorasExtras { get; set; }

        // Novas propriedades adicionadas
        public TimeSpan? HorasTrabalhadas { get; set; }
        public TimeSpan? Atraso { get; set; }
        public bool HoraExtraAutorizada { get; set; }

        // Horários previstos INDIVIDUAIS para cada funcionário
        public TimeSpan? HorarioPrevistoEntrada { get; set; }
        public TimeSpan? HorarioPrevistoSaida { get; set; }

        public string Turno { get; set; }
        public string Observacoes { get; set; }
        public bool Ausente { get; set; }
        public string TipoAusencia { get; set; }
        public string JustificativaAusencia { get; set; }
        public string JustificativaHorasExtras { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation property
        public virtual User User { get; set; }
    }

    // Models/HorarioUsuario.cs
    public class HorarioUsuario
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public bool Segunda { get; set; }
        public bool Terca { get; set; }
        public bool Quarta { get; set; }
        public bool Quinta { get; set; }
        public bool Sexta { get; set; }
        public bool Sabado { get; set; }
        public bool Domingo { get; set; }
        public int DiasTrabalhoSemana { get; set; }
        public int HorasPorDia { get; set; }

        // Horários específicos para cada dia da semana
        public TimeSpan? SegundaEntrada { get; set; }
        public TimeSpan? SegundaSaida { get; set; }
        public TimeSpan? TercaEntrada { get; set; }
        public TimeSpan? TercaSaida { get; set; }
        public TimeSpan? QuartaEntrada { get; set; }
        public TimeSpan? QuartaSaida { get; set; }
        public TimeSpan? QuintaEntrada { get; set; }
        public TimeSpan? QuintaSaida { get; set; }
        public TimeSpan? SextaEntrada { get; set; }
        public TimeSpan? SextaSida { get; set; }
        public TimeSpan? SabadoEntrada { get; set; }
        public TimeSpan? SabadoSaida { get; set; }
        public TimeSpan? DomingoEntrada { get; set; }
        public TimeSpan? DomingoSaida { get; set; }

        public TimeSpan HorarioPadraoEntrada { get; set; } = TimeSpan.Parse("09:00");
        public TimeSpan HorarioPadraoSaida { get; set; } = TimeSpan.Parse("18:00");

        public DateTime VigenciaInicio { get; set; }
        public DateTime? VigenciaFim { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public bool Ativo { get; set; } = true;

        public virtual User User { get; set; }
    }
}