using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Components;

public partial class StatCard : ComponentBase
{
    [Parameter] public required string Title { get; set; }
    [Parameter] public required double Value { get; set; }
    [Parameter] public required double ReferenceValue { get; set; }
    [Parameter] public bool Inverse { get; set; }

    private string _icon = "arrow-right";
    private string _iconClass = "ph ph-arrow-right";
    private string _iconColor = "text-slate-400";
    private string _textColor = "text-slate-400";
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
            var isPositive = Inverse ? Value < ReferenceValue : Value > ReferenceValue;
            _iconColor = isPositive ? "text-green-400" : "text-red-400";
            _textColor = isPositive ? "text-green-400" : "text-red-400";
            _icon = Value > ReferenceValue ? "arrow-up" : "arrow-down";
            _iconClass = $"ph ph-{_icon}";
        }
        else
        {
            _icon = "arrow-right";
            _iconClass = "ph ph-arrow-right";
            _iconColor = "text-slate-400";
            _textColor = "text-slate-400";
        }

        _percentage = Math.Round((decimal)Value / (decimal)ReferenceValue - 1, 4);
        StateHasChanged();
    }
}
