namespace BazaarCompanionWeb.Dtos;

public class ProductCorrelation
{
    public string ProductKey1 { get; set; } = string.Empty;
    public string ProductName1 { get; set; } = string.Empty;
    public string ProductKey2 { get; set; } = string.Empty;
    public string ProductName2 { get; set; } = string.Empty;
    public double CorrelationCoefficient { get; set; }
}

public class CorrelationMatrix
{
    public List<string> ProductKeys { get; set; } = new();
    public List<string> ProductNames { get; set; } = new();
    public Dictionary<string, Dictionary<string, double>> Matrix { get; set; } = new();
    public DateTime CalculatedAt { get; set; }
}

public class RelatedProduct
{
    public string ProductKey { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double Correlation { get; set; }
    public string CorrelationType { get; set; } = string.Empty; // "Strong", "Moderate", "Weak"
}
