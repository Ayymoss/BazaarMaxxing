using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Shared;

public partial class Modal : ComponentBase
{
    [Parameter] public bool Show { get; set; }
    [Parameter] public EventCallback<bool> ShowChanged { get; set; }
    [Parameter] public bool ShowClose { get; set; } = true;
    [Parameter] public bool CloseOnBackdropClick { get; set; } = true;
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private async Task Close()
    {
        Show = false;
        await ShowChanged.InvokeAsync(false);
    }

    private async Task HandleBackdropClick()
    {
        if (CloseOnBackdropClick)
        {
            await Close();
        }
    }
}
