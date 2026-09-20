using System.Windows.Media;

namespace AyGestRest.Views
{
    internal class BackupInfo
    {
        public string Data { get; set; }
        public string Hora { get; set; }
        public string Tamanho { get; set; }
        public string Status { get; set; }
        public SolidColorBrush StatusCor { get; set; }
    }
}