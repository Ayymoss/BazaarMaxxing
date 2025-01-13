namespace BazaarCompanion.Utilities;

public class Configuration
{
    public string HyPixelApikey { get; set; } = "Change Me";
    public int MinimumMargin { get; set; } = 250;
    public float MinimumPotentialProfitMultiplier { get; set; } = 2;
    public float MinimumBuyOrderPower { get; set; } = 0.5f;
    public long MinimumWeekVolume { get; set; } = 50_000;
}
