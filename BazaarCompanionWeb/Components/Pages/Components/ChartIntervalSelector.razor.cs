using BazaarCompanionWeb.Entities;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Components;

public partial class ChartIntervalSelector
{
    [Parameter] public CandleInterval SelectedInterval { get; set; }
    [Parameter] public EventCallback<CandleInterval> OnIntervalChanged { get; set; }
    private bool _isOpen;
    private void Toggle() => _isOpen = !_isOpen;

    private async Task SelectInterval(CandleInterval interval)
    {
        _isOpen = false;
        if (SelectedInterval != interval)
        {
            SelectedInterval = interval;
            await OnIntervalChanged.InvokeAsync(interval);
        }

        StateHasChanged();
    }

    private void Close()
    {
        _isOpen = false;
        StateHasChanged();
    }

    private static string GetShortName(CandleInterval interval) => interval switch
    {
        CandleInterval.FiveMinute => "5M",
        CandleInterval.FifteenMinute => "15M",
        CandleInterval.OneHour => "1H",
        CandleInterval.FourHour => "4H",
        CandleInterval.OneDay => "1D",
        CandleInterval.OneWeek => "1W",
        _ => interval.ToString()
    };
}