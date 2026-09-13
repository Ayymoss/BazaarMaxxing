using BazaarCompanionWeb.Entities;
namespace BazaarCompanionWeb.Services.Ingestion;
public sealed record ProductObservation(EFProduct Product, DateTime? UpstreamUtc);
