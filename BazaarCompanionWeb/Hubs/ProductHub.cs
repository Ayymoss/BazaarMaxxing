using Microsoft.AspNetCore.SignalR;

namespace BazaarCompanionWeb.Hubs;

public sealed class ProductHub : Hub
{
    public async Task JoinProductGroup(string productKey)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, productKey);
    }

    public async Task LeaveProductGroup(string productKey)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, productKey);
    }
}
