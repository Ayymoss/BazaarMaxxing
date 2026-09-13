namespace BazaarCompanionWeb.Dtos;

/// <summary>
/// One candle. <see cref="Volume"/> is units traded (<see cref="BuyVolume"/> + <see cref="SellVolume"/>), not
/// order-book depth. Ask open/high/low are zero where the source only kept the ask close.
/// </summary>
public record OhlcDataPoint(
    DateTime Time,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume,
    double Spread,
    double AskClose,
    double BuyVolume = 0,
    double SellVolume = 0,
    double AskOpen = 0,
    double AskHigh = 0,
    double AskLow = 0,
    double? EstimatedVolume = null);
