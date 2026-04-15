using System.Collections.Generic;

namespace FlexiFit.Api.Entities
{
    public class NtrAllergies
    {
        public int AllergyId { get; set; }
        public string AllergyName { get; set; }

        public virtual ICollection<NtrUserAllergies> UserAllergies { get; set; }
        public virtual ICollection<NtrFoodAllergies> FoodAllergies { get; set; }
    }
}