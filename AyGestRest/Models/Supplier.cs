using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AyGestRest.Models
{
    public class Supplier
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string NIF { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string Province { get; set; }
        public string ContactPerson { get; set; }
        public string PaymentTerms { get; set; }
        public string Notes { get; set; }
        public bool Active { get; set; } = true;

        // Propriedade de navegação
        public ICollection<PurchaseOrder> PurchaseOrders { get; set; }

        // Propriedade calculada (não mapeada)
        [NotMapped]
        public int PurchaseCount { get; set; }
    }

}
