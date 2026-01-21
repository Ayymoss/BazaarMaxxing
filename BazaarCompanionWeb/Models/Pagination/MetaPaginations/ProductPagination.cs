using BazaarCompanionWeb.Models.Pagination;

namespace BazaarCompanionWeb.Models.Pagination.MetaPaginations;

public class ProductPagination : Pagination
{
    public bool ToggleFilter { get; set; }
    public AdvancedFilterOptions? AdvancedFilters { get; set; }
    public bool UseFuzzySearch { get; set; } = false;
}
