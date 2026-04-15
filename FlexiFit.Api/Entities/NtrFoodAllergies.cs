using System.ComponentModel.DataAnnotations.Schema;

namespace FlexiFit.Api.Entities
{
    public class NtrFoodAllergies
    {
        [Column("food_id")]
        public int FoodId { get; set; }

        [Column("allergy_id")]
        public int AllergyId { get; set; }

        public virtual NtrFoodItem Food { get; set; } = null!;
        public virtual NtrAllergies Allergy { get; set; } = null!;
    }
}