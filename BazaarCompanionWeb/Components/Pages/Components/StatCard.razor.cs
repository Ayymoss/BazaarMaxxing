using Microsoft.AspNetCore.Components;
using Radzen;

namespace BazaarCompanionWeb.Components.Pages.Components;

public partial class StatCard : ComponentBase
{
    [Parameter] public Variant Variant { get; set; } = Variant.Filled;
    [Parameter] public required string Title { get; set; }
    [Parameter] public required double Value { get; set; }
    [Parameter] public required double ReferenceValue { get; set; }
    [Parameter] public bool Inverse { get; set; }

    private string Icon { get; set; } = "trending_flat";
    private string Color { get; set; } = "rz-base";
    private double Percentage { get; set; }


    protected override void OnInitialized()
    {
        if (Math.Abs(ReferenceValue / Value - 1) > 0.05)
        {
            Color = (Inverse ? Value < ReferenceValue : Value > ReferenceValue) ? "rz-color-success-light" : "rz-color-danger-light";
            Icon = Value > ReferenceValue ? "trending_up" : "trending_down";
        }

        Percentage = Value / ReferenceValue - 1;
    }
}
