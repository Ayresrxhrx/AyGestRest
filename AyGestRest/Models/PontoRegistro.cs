// Adicione estes modelos ao seu projeto:

namespace AyGestRest.Models
{
    public class PontoRegistro
    {
        public int Id { get; set; }
        public int UserId { get; set; } // Funcionário
        public DateTime Data { get; set; }
        public DateTime? HoraEntrada { get; set; }
        public DateTime? HoraSaida { get; set; }
        public DateTime? HoraEntradaAlmoco { get; set; }
        public DateTime? HoraSaidaAlmoco { get; set; }
        public string? Observacoes { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Propriedades calculadas para UI
        public string EstadoTexto
        {
            get
            {
                if (HoraEntrada == null && HoraSaida == null)
                    return "Não Iniciado";
                if (HoraEntrada != null && HoraSaida == null)
                    return "Em Trabalho";
                if (HoraSaida != null)
                    return "Concluído";
                return "Ausente";
            }
        }

        public string EstadoCor
        {
            get
            {
                if (HoraEntrada == null && HoraSaida == null)
                    return "#6B7280"; // Cinza
                if (HoraEntrada != null && HoraSaida == null)
                    return "#10B981"; // Verde
                if (HoraSaida != null)
                    return "#3B82F6"; // Azul
                return "#EF4444"; // Vermelho
            }
        }

        public string? NomeFuncionario { get; set; } // Para exibição
        public TimeSpan? HorasTrabalhadas
        {
            get
            {
                if (HoraEntrada == null || HoraSaida == null)
                    return null;

                TimeSpan horasTotais = HoraSaida.Value - HoraEntrada.Value;

                // Subtrair horas de almoço se existirem
                if (HoraEntradaAlmoco != null && HoraSaidaAlmoco != null)
                {
                    TimeSpan horasAlmoco = HoraSaidaAlmoco.Value - HoraEntradaAlmoco.Value;
                    horasTotais -= horasAlmoco;
                }

                return horasTotais;
            }
        }

        public bool PodeRegistrarEntrada => HoraEntrada == null;
        public bool PodeRegistrarSaida => HoraEntrada != null && HoraSaida == null;
        public bool PodeRegistrarAlmoco => HoraEntrada != null && HoraSaida == null;
    }
}