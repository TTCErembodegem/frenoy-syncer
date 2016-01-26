using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace Ttc.Model
{
    [Table("verslag")]
    public class Verslag
    {
        [Key]
        public int ID { get; set; }

        // Foreign Key voor Kalender
        public int KalenderID { get; set; }

        // Foreign Key voor Speler
        public int SpelerID { get; set; }

        [ForeignKey("KalenderID")]
        public Kalender Kalender { get; set; }

        //[ForeignKey("SpelerID")]
        //public Speler Speler { get; set; }

        public string Beschrijving { get; set; }

        public int? UitslagThuis { get; set; }

        public int? UitslagUit { get; set; }

        public int WO { get; set; }

        public int? Details { get; set; }
    }
}
