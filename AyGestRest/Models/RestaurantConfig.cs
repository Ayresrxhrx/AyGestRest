using System.ComponentModel.DataAnnotations;

namespace AyGestRest.Models
{
    public class RestaurantConfig
    {
        [Key]
        public int Id { get; set; }

        // Obrigatório – tem valor padrão
        public string RestaurantName { get; set; } = "Meu Restaurante";

        // Campos opcionais do restaurante
        public string? NIF { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }

        // Email público do restaurante (opcional)
        public string? Email { get; set; }

        // ========================
        // CONFIGURAÇÃO DE EMAIL PARA ENVIO
        // ========================
        public string? EmailAddress { get; set; }              // Email de envio (ex: seuemail@gmail.com)

        public string EmailPassword { get; set; } = "";   // 🔥 ADICIONE VALOR PADRÃO
        public string? SMTPServer { get; set; } = "smtp.gmail.com";
        public int SMTPPort { get; set; } = 587;
        public bool UseSSL { get; set; } = true;

        // Email destinatário para relatórios
        public string? RecipientEmail { get; set; }

        // Configurações de notificação
        public bool SendDailyReport { get; set; } = true;      // Enviar fecho de dia
        public bool SendReports { get; set; } = true;          // Enviar relatórios

        // Moeda – tem valor padrão
        public string CurrencySymbol { get; set; } = "MT";

        // ========================
        // LOGO – TODOS OPCIONAIS
        // ========================
        public string? LogoPath { get; set; }          // Caminho interno (ex: "restaurant_logo.png")
        public byte[]? LogoBytes { get; set; }         // Opcional – se quiseres guardar como BLOB
        public string? LogoMimeType { get; set; }      // ex: "image/png" – agora nullable

        // ========================
        // SEGURANÇA
        // ========================
        public bool RequiresLogin { get; set; } = true;           // Removi o ? porque é bool normal
        public int InactivityTimeout { get; set; } = 15;
        public bool AutoSave { get; set; } = true;

        // ========================
        // PERMISSÕES DO FUNCIONÁRIO
        // ========================
        public bool FuncionarioPodeFechoDiario { get; set; } = false;
        public bool FuncionarioPodeReservas { get; set; } = false;
        public bool FuncionarioPodeHistoricoVendas { get; set; } = false;

        // ========================
        // INTERFACE
        // ========================
        public bool SoundEffects { get; set; } = true;
        public bool DarkMode { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;

        // ========================
        // BACKUP
        // ========================
        public bool BackupDiario { get; set; } = true;
        public bool AutoBackup { get; set; } = false;
        public string BackupTime { get; set; } = "23:00";
        public string? BackupPath { get; set; }                // Agora opcional

        // ========================
        // IMPRESSÃO
        // ========================
        public string? PrinterName { get; set; }               // Pode ficar sem impressora
        public string? KitchenPrinterName { get; set; }        // Pode ficar sem impressora
        public bool PrintOrderToKitchen { get; set; } = true;

        // ========================
        // TELA SECUNDÁRIA
        // ========================
        public bool SecondScreenEnabled { get; set; } = false;

        // WhatsApp (mantive se ainda precisar)
        public string? WhatsAppToken { get; set; }
        public string? WhatsAppPhoneId { get; set; }
        [Range(1, 10, ErrorMessage = "O número de cópias deve estar entre 1 e 10")]
        public int CopiesCount { get; set; } = 1; // Valor padrão: 1 cópia

        public string PrintMode { get; set; } = "Sequential";
        public string? WhatsAppFullNumber { get; set; }
        public bool ImprimirAntesDeFechar { get; set; }
    }
}