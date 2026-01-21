using System.Linq.Expressions;
using System.Reflection;
using BazaarCompanionWeb.Enums;
using BazaarCompanionWeb.Models.Api.Items;
using BazaarCompanionWeb.Models.Pagination;

namespace BazaarCompanionWeb.Utilities;

public static class HelperMethods
{
    public static string GetVersion() => Assembly.GetCallingAssembly().GetName().Version?.ToString() ?? "Unknown";

    public static IQueryable<TDomain> ApplySort<TDomain>(this IQueryable<TDomain> query, SortDescriptor sort,
        Expression<Func<TDomain, object>> property) => sort.SortOrder is SortDirection.Ascending
        ? query.OrderBy(property)
        : query.OrderByDescending(property);

    public static IQueryable<TEfProduct> ApplySortForName<TEfProduct>(this IQueryable<TEfProduct> query, SortDescriptor sort,
        Expression<Func<TEfProduct, string>> nameProperty, Expression<Func<TEfProduct, ItemTier>> tierProperty)
    {
        return sort.SortOrder == SortDirection.Ascending
            ? query.OrderBy(tierProperty).ThenBy(nameProperty)
            : query.OrderByDescending(tierProperty).ThenBy(nameProperty);
    }

    public static string ProductTierColor(this ItemTier itemTier)
    {
        return itemTier switch
        {
            ItemTier.Common => "#FFFFFF",
            ItemTier.Uncommon => "#78F86A",
            ItemTier.Rare => "#535FF8",
            ItemTier.Epic => "#A22EA5",
            ItemTier.Legendary => "#F9AD35",
            ItemTier.Mythic => "#F46DF9",
            ItemTier.Supreme => "#76FBFE",
            ItemTier.Special => "#F5655A",
            ItemTier.VerySpecial => "#F5655A",
            ItemTier.Unobtainable => "#A2240F",
            _ => "#808080"
        };
    }

    public static string MultiplierColor(this double multiplier)
    {
        return multiplier switch
        {
            < 2 => "#808080",
            < 3 => "#FFFFFF",
            < 5 => "#78F86A",
            < 10 => "#535FF8",
            < 50 => "#A22EA5",
            < 100 => "#F9AD35",
            < 500 => "#F46DF9",
            _ => "#76FBFE"
        };
    }

    public static string RatingColor(this double reference)
    {
        return reference switch
        {
            < 0.01 => "#808080",
            < 0.5 => "#FFFFFF",
            < 1 => "#78F86A",
            < 2 => "#535FF8",
            < 3 => "#A22EA5",
            < 4 => "#F9AD35",
            < 5 => "#F46DF9",
            _ => "#76FBFE"
        };
    }

    /// <summary>
    /// Returns a color for manipulation intensity, ranging from orange to red as intensity increases.
    /// </summary>
    /// <param name="intensity">Manipulation intensity from 0.0 to 1.0</param>
    /// <returns>Hex color code</returns>
    public static string ManipulationColor(this double intensity)
    {
        // Gradient from orange (#F97316) to red (#EF4444) based on intensity
        return intensity switch
        {
            < 0.33 => "#F97316", // Orange
            < 0.66 => "#FB923C", // Light orange
            _ => "#EF4444"        // Red for high intensity
        };
    }
}
