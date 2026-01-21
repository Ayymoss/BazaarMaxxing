namespace BazaarCompanionWeb.Models.Pagination;

public class FilterPreset
{
    public string Name { get; set; } = string.Empty;
    public AdvancedFilterOptions Filters { get; set; } = new();
    public string? SearchQuery { get; set; }
    public List<SortDescriptor> Sorts { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastUsed { get; set; }
}
