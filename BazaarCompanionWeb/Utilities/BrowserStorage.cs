using System.Text.Json;
using Microsoft.JSInterop;

namespace BazaarCompanionWeb.Utilities;

public class BrowserStorage(IJSRuntime jsRuntime)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    
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
    
    /// <summary>
    /// Get a typed object from localStorage (deserializes from JSON)
    /// </summary>
    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string>("localStorage.getItem", key);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Set a typed object in localStorage (serializes to JSON)
    /// </summary>
    public async Task SetAsync<T>(string key, T value)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", key, json);
        }
        catch
        {
            // Ignore storage errors
        }
    }
}
