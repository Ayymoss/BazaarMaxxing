namespace BazaarCompanionWeb.Models;

public class OrderInfo
{
    public required double? UnitPrice { get; set; }
    public required double WeekVolume { get; set; }
    public required int CurrentOrders { get; set; }
    public required int CurrentVolume { get; set; }
}
