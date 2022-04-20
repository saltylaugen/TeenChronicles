using System.Collections.Generic;

namespace Nekoyume.EnumType
{
    // [TEN Code Block Start]
    public enum ShopSortFilter
    {
        Time = 0,
        Class = 1,
        CP = 2,
        Price = 3,
        Level = 4,
    }
    // [TEN Code Block End]

    public static class ShopSortFilterExtension
    {
        public static IEnumerable<ShopSortFilter> ShopSortFilters
        {
            get
            {
                return new[]
                // [TEN Code Block Start]
                {
                    ShopSortFilter.Time,
                    ShopSortFilter.Class,
                    ShopSortFilter.CP,
                    ShopSortFilter.Price,
                    ShopSortFilter.Level,
                };
                // [TEN Code Block End]
            }
        }
    }
}
