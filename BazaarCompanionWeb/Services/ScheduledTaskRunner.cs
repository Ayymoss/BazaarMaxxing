namespace BazaarCompanionWeb.Services;

public class ScheduledTaskRunner(IServiceProvider serviceProvider, ILogger<ScheduledTaskRunner> logger) : IDisposable
{
    private Timer? _timer;
    private bool _firstRun = true;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public void StartTimer()
    {
        _timer = new Timer(ScheduleSteamActions, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }

    private void ScheduleSteamActions(object? state)
    {
#if !DEBUG
        if (_firstRun)
        {
            _firstRun = false;
            return;
        }
#endif

        Task.Run(async () =>
        {
            using var scope = serviceProvider.CreateScope();
            var hyPixelService = scope.ServiceProvider.GetRequiredService<HyPixelService>();
            try
            {
                logger.LogInformation("[SCHEDULED - STARTING] Scheduled action");

                logger.LogInformation("Populating products");
                await hyPixelService.FetchDataAsync(_cancellationTokenSource.Token);

                logger.LogInformation("[SCHEDULED - FINISHED] Scheduled action");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error executing scheduled action");
            }
        }, _cancellationTokenSource.Token);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _cancellationTokenSource.Cancel();
    }
}
