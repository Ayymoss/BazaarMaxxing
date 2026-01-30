namespace BazaarCompanionWeb.Dtos;

public record LiveTick(
    DateTime Time, 
    double Open, 
    double High, 
    double Low, 
    double Close, 
    double Volume,
    double AskClose);
