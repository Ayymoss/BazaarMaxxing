namespace BazaarCompanionWeb.Dtos;

public record OhlcDataPoint(DateTime Time, double Open, double High, double Low, double Close, double? Volume = null);
