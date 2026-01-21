using Microsoft.JSInterop;

namespace BazaarCompanionWeb.Utilities;

public class BrowserStorage(IJSRuntime jsRuntime)
{
    public async Task<string?> GetItemAsync(string key)
    {
        try
        {
            return await jsRuntime.InvokeAsync<string>("localStorage.getItem", key);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetItemAsync(string key, string value)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch
        {
            // Ignore storage errors
        }
    }

    public async Task RemoveItemAsync(string key)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch
        {
            // Ignore storage errors
        }
    }
}
