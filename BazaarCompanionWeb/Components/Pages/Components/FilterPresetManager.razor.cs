using System.Text.Json;
using BazaarCompanionWeb.Models.Pagination;
using Microsoft.AspNetCore.Components;

namespace BazaarCompanionWeb.Components.Pages.Components;

public partial class FilterPresetManager
{
    [Parameter] public required AdvancedFilterOptions CurrentFilters { get; set; }
    [Parameter] public EventCallback<AdvancedFilterOptions> PresetLoaded { get; set; }
    private List<FilterPreset> _presets = new();
    private bool _showSaveDialog = false;
    private string _newPresetName = string.Empty;
    private const string PresetStorageKey = "bazaar_filter_presets";

    protected override async Task OnInitializedAsync()
    {
        await LoadPresetsAsync();
    }

    private async Task LoadPresetsAsync()
    {
        try
        {
            var stored = await BrowserStorage.GetItemAsync(PresetStorageKey);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                _presets = JsonSerializer.Deserialize<List<FilterPreset>>(stored) ?? new List<FilterPreset>();
            }
        }
        catch
        {
            _presets = new List<FilterPreset>();
        }
    }

    private async Task SavePresetsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_presets);
            await BrowserStorage.SetItemAsync(PresetStorageKey, json);
        }
        catch
        {
            // Ignore storage errors
        }
    }

    private void ShowSaveDialog()
    {
        _showSaveDialog = true;
        _newPresetName = string.Empty;
    }

    private void CancelSave()
    {
        _showSaveDialog = false;
        _newPresetName = string.Empty;
    }

    private async Task SavePreset()
    {
        if (string.IsNullOrWhiteSpace(_newPresetName))
            return;

        var preset = new FilterPreset
        {
            Name = _newPresetName,
            Filters = JsonSerializer.Deserialize<AdvancedFilterOptions>(
                JsonSerializer.Serialize(CurrentFilters)) ?? new AdvancedFilterOptions(),
            CreatedAt = DateTime.Now
        };

        // Remove existing preset with same name
        _presets.RemoveAll(p => p.Name == preset.Name);
        _presets.Add(preset);

        await SavePresetsAsync();
        _showSaveDialog = false;
        _newPresetName = string.Empty;
        StateHasChanged();
    }

    private async Task LoadPreset(ChangeEventArgs e)
    {
        var presetName = e.Value?.ToString();
        if (!string.IsNullOrWhiteSpace(presetName))
        {
            await LoadPresetByName(presetName);
        }
    }

    private async Task LoadPresetByName(string presetName)
    {
        var preset = _presets.FirstOrDefault(p => p.Name == presetName);
        if (preset != null)
        {
            var loadedFilters = JsonSerializer.Deserialize<AdvancedFilterOptions>(
                JsonSerializer.Serialize(preset.Filters)) ?? new AdvancedFilterOptions();

            preset.LastUsed = DateTime.Now;
            await SavePresetsAsync();

            await PresetLoaded.InvokeAsync(loadedFilters);
        }
    }

    private async Task DeletePreset(string presetName)
    {
        _presets.RemoveAll(p => p.Name == presetName);
        await SavePresetsAsync();
        StateHasChanged();
    }
}