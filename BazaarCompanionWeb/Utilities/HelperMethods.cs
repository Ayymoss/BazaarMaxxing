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
}
