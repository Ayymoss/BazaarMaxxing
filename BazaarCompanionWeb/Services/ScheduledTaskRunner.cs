namespace BazaarCompanionWeb.Services;

public class ScheduledTaskRunner(IServiceProvider serviceProvider, ILogger<ScheduledTaskRunner> logger) : IDisposable
{
    private Timer? _timer;
    private bool _firstRun = true;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    public const int TimerMinutes = 1;

    public void StartTimer()
    {
        _timer = new Timer(ScheduleSteamActions, null, TimeSpan.Zero, TimeSpan.FromMinutes(TimerMinutes));
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
                // HyPixelService logs its own per-poll summary; the scheduler frame just guards the call.
                await hyPixelService.FetchDataAsync(_cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error executing scheduled poll");
            }
        }, _cancellationTokenSource.Token);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _cancellationTokenSource.Cancel();
    }
}
