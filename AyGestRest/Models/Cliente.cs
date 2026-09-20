using System;
using System.ComponentModel.DataAnnotations;

namespace AyGestRest.Models
{
    public class Cliente
    {
        [Key]
        public int Id { get; set; }

        public string CodigoUnico { get; set; } = string.Empty;

        public string Nome { get; set; } = string.Empty;

        public string? Telefone { get; set; }

        public string? Email { get; set; }

        public int Pontos { get; set; } = 0;

        public DateTime UltimaVisita { get; set; } = DateTime.Today;

        // Propriedade para mostrar no ComboBox de forma mais completa
        public string DisplayName =>
            string.IsNullOrWhiteSpace(Telefone)
                ? $"{Nome} ({CodigoUnico}) - Pontos: {Pontos}"
                : $"{Nome} ({CodigoUnico}) - Tel: {Telefone} - Pontos: {Pontos}";
    }
}