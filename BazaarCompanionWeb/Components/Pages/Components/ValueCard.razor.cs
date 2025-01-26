using Microsoft.AspNetCore.Components;
using Radzen;

namespace BazaarCompanionWeb.Components.Pages.Components;

public partial class ValueCard : ComponentBase
{
    [Parameter] public Variant Variant { get; set; } = Variant.Filled;
    [Parameter] public required string Title { get; set; }
    [Parameter] public required string Value { get; set; }
    [Parameter] public string ValueCss { get; set; } = string.Empty;
    [Parameter] public string ValueStyle { get; set; } = string.Empty;
}
