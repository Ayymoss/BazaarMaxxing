namespace BazaarCompanionWeb.Dtos;

public record ManipulationScore(
    bool IsManipulated,      // True if Z-score exceeds threshold
    double ZScore,            // Raw Z-score value
    double DeviationPercent, // Percentage deviation from mean
    double ManipulationIntensity // 0-1 scale of manipulation strength
);
