using Radzen.Blazor;

namespace BazaarCompanionWeb.Components.Layout;

public partial class MainLayout : IAsyncDisposable
{
    private RadzenBody? _body;
    private TopBar? _topBar;

    public async ValueTask DisposeAsync()
    {
        _body?.Dispose();
    }
}
