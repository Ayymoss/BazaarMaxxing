using BazaarCompanionWeb.Enums;

namespace BazaarCompanionWeb.Models.Pagination;

public class SortDescriptor
{
    public string Property { get; set; }
    public SortDirection SortOrder { get; set; }
}
