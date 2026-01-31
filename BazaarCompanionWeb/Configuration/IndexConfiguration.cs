namespace BazaarCompanionWeb.Configurations;

public class IndexConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public List<string> ProductKeys { get; set; } = new();
}
