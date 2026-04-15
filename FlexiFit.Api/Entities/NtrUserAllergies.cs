using System.ComponentModel.DataAnnotations.Schema;

namespace FlexiFit.Api.Entities
{
    public class NtrUserAllergies
    {
        [Column("user_id")]
        public int UserId { get; set; }

        [Column("allergy_id")]
        public int AllergyId { get; set; }

        public virtual UsrUser User { get; set; } = null!;
        public virtual NtrAllergies Allergy { get; set; } = null!;
    }
}   