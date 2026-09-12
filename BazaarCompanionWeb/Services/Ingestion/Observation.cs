namespace BazaarCompanionWeb.Services.Ingestion;

/// <summary>
/// When one product was last seen: the poll that carried it, and the time Hypixel stamped on that poll's
/// market snapshot. A product that has dropped out of the feed keeps the observation it had, so its age is
/// its own and not the age of whatever poll landed last.
/// </summary>
public sealed record Observation(DateTime ObservedUtc, DateTime UpstreamUtc);
