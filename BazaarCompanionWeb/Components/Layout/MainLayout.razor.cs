namespace BazaarCompanionWeb.Components.Layout;

public partial class MainLayout : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
