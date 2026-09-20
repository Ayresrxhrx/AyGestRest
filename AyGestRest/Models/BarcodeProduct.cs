using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AyGestRest.Models
{
    // Classe para binding na grid de código de barras
    public class BarcodeProduct
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public bool HasBarcode { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public decimal Stock { get; set; }
        public string Barcode { get; set; }
        public bool IsSelected { get; set; }
        public BitmapImage BarcodeImage { get; set; }
    }
}
