using System.Globalization;
using BazaarCompanionWeb.Dtos;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Dialogs.Components;

public partial class PriceGraph : ComponentBase
{
    [Parameter] public required ProductDataInfo Product { get; set; }

    private static string AxisFormat(object arg) => arg is DateOnly dateOnly ? dateOnly.ToString("yyyy-MM-dd") : string.Empty;
    private static string ValueFormat(object arg) => ((double)arg).ToString("C0", CultureInfo.CreateSpecificCulture("en-US"));
}
