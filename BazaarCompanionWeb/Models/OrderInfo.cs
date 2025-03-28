namespace BazaarCompanionWeb.Models;

public class OrderInfo
{
    public required double Last { get; set; }
    public required double OrderPrice { get; set; }
    public required double WeekVolume { get; set; }
    public required int CurrentOrders { get; set; }
    public required int CurrentVolume { get; set; }
    public IEnumerable<OrderBook> OrderBook { get; set; }
}
