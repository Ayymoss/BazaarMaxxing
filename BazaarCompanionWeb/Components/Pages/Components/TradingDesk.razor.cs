using BazaarCompanionWeb.Dtos;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Components;

public partial class TradingDesk : ComponentBase
{
    [Parameter, EditorRequired] public required ProductDataInfo Product { get; set; }
    [Parameter] public double BuyAverage { get; set; }
    [Parameter] public double SellAverage { get; set; }
}
