namespace BazaarCompanionWeb.Components.Layout;

public partial class MainLayout : IAsyncDisposable
{
    private TopBar? _topBar;

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
