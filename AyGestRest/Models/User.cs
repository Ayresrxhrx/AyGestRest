

namespace AyGestRest.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;  // Nunca guardar senha em texto puro!
        public string FullName { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public bool IsActive { get; set; } = true;

        // 🔥 PROPRIEDADES PARA RELATÓRIOS
        public string NomeOrdenacao { get; set; } = string.Empty;

        public string DisplayName => !string.IsNullOrEmpty(FullName) ? FullName : Username;

        public DateTime? UltimoLogin { get; internal set; }
        public DateTime? LastLogin { get; internal set; }
        public string? Name { get; internal set; }
        public DateTime UpdatedAt { get; internal set; }
        public DateTime CreatedAt { get; internal set; }
    }
}