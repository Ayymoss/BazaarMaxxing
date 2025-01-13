using Microsoft.Extensions.Hosting;

namespace BazaarCompanion;

public class AppEntry(BazaarDisplay bazaarDisplay) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        new Thread(async () => await bazaarDisplay.HandleDisplayAsync(cancellationToken)) { Name = nameof(AppEntry) }.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
