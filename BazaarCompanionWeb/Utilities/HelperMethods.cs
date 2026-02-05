using System.Linq.Expressions;
using System.Reflection;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Enums;
using BazaarCompanionWeb.Models.Api.Items;
using BazaarCompanionWeb.Models.Pagination;
using Humanizer;

namespace BazaarCompanionWeb.Utilities;

public static class HelperMethods
{
    public static string GetVersion() => Assembly.GetCallingAssembly().GetName().Version?.ToString() ?? "Unknown";

    public static string ToCompactString(this double value, bool isPercentage = false)
    {
        if (!isPercentage)
        {
            return Math.Abs(value) >= 1_000_000
                ? value.ToMetric(decimals: 2)
                : value.ToString("N2");
        }

        // For percentages, standard practice is value 0.25 = 25%
        var displayValue = value * 100;
        var absDisplayValue = Math.Abs(displayValue);

        return absDisplayValue switch
        {
            // Rule: If >= 1M% (very rare in Bazaar, but handled)
            >= 1_000_000 => $"{displayValue.ToMetric(decimals: 0)}%",

            // Rule: If >= 100% (e.g. 200% profit), drop decimals
            >= 100 => $"{displayValue:N0}%",

            // Rule: Small percentages (e.g. 0.34%), keep precision
            // This handles the "0.34%" bug by using displayValue instead of raw value
            _ => $"{displayValue:0.##}%"
        };
    }

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
            _ => "#EF4444" // Red for high intensity
        };
    }

    public static DateTime GetPeriodStart(this DateTime timestamp, CandleInterval interval)
    {
        if (interval == CandleInterval.OneWeek)
        {
            // Start of week (Monday)
            var diff = (7 + (timestamp.DayOfWeek - DayOfWeek.Monday)) % 7;
            return timestamp.AddDays(-1 * diff).Date;
        }

        if (interval == CandleInterval.OneDay)
        {
            return timestamp.Date;
        }

        var intervalMinutes = (int)interval;
        var totalMinutesSinceEpoch = (long)(timestamp - DateTime.UnixEpoch).TotalMinutes;
        var periodMinutes = totalMinutesSinceEpoch / intervalMinutes * intervalMinutes;
        return DateTime.UnixEpoch.AddMinutes(periodMinutes);
    }
}
