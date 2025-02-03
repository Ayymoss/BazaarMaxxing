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

    private string _icon = "trending_flat";
    private string _color = "rz-base";
    private decimal _percentage;

    protected override void OnInitialized()
    {
        UpdateCalculation();
    }

    public void UpdateValues(double value, double reference)
    {
        Value = value;
        ReferenceValue = reference;
        UpdateCalculation();
    }

    private void UpdateCalculation()
    {
        if (Math.Abs(ReferenceValue / Value - 1) > 0.05)
        {
            _color = (Inverse ? Value < ReferenceValue : Value > ReferenceValue) ? "rz-color-success-light" : "rz-color-danger-light";
            _icon = Value > ReferenceValue ? "trending_up" : "trending_down";
        }
        else
        {
            _icon = "trending_flat";
            _color = "rz-base";
        }

        _percentage = Math.Round((decimal)Value / (decimal)ReferenceValue - 1, 2);
        StateHasChanged();
    }
}
