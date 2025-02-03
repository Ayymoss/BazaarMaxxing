namespace BazaarCompanionWeb.Utilities;

public class TimeCache
{
    public DateTimeOffset LastUpdated { get; set; } = TimeProvider.System.GetLocalNow();
}
