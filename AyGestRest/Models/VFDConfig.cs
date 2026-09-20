namespace AyGestRest.Models
{
    public class VFDConfig
    {
        public int Id { get; set; }
        public bool IsEnabled { get; set; } = false;
        public string COMPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public int DisplayLines { get; set; } = 2;
        public int DisplayColumns { get; set; } = 20;
        public bool ShowRealTimeTotal { get; set; } = true;
        public string LastTestDate { get; set; } = string.Empty;
    }
}