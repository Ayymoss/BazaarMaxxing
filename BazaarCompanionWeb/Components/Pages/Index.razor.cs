using BazaarCompanionWeb.Configurations;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Models.Api.Items;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Serilog;

namespace BazaarCompanionWeb.Components.Pages;

public partial class Index(
    IOptions<List<IndexConfiguration>> indexOptions,
    IProductRepository productRepository) : ComponentBase
{
    [Parameter] public required string IndexSlug { get; set; }

    private IndexConfiguration? _index;
    private bool _loading = true;
    private CandleInterval _selectedInterval = CandleInterval.OneHour;

    private List<UnderlyingProductInfo> _underlyingProducts = [];
    private List<string> _missingProductKeys = [];

    protected override async Task OnInitializedAsync()
    {
        await LoadIndexDataAsync();
    }

    private async Task LoadIndexDataAsync(CancellationToken ct = default)
    {
        try
        {
            // Find the index by slug from configuration
            var indices = indexOptions.Value;
            _index = indices.FirstOrDefault(i => i.Slug.Equals(IndexSlug, StringComparison.OrdinalIgnoreCase));

            if (_index is null)
            {
                Log.Warning("Index with slug '{Slug}' not found", IndexSlug);
                return;
            }

            // Load product info for underlying products
            await LoadUnderlyingProductsAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading index data for {Slug}", IndexSlug);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadUnderlyingProductsAsync(CancellationToken ct = default)
    {
        if (_index is null) return;

        var resolvedKeys = await productRepository.GetProductKeysMatchingAsync(_index.ProductKeys, ct);
        if (resolvedKeys.Count == 0)
        {
            _underlyingProducts = [];
            _missingProductKeys = _index.ProductKeys
                .Where(k => !k.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return;
        }

        // Exclude low-volume products (matches IndexAggregationService; avoids truncation from pruned items)
        const double minVolume = 100;
        var volumeFilteredKeys = await productRepository.GetProductKeysWithMinVolumeAsync(resolvedKeys, minVolume, ct);
        if (volumeFilteredKeys.Count == 0)
        {
            _underlyingProducts = [];
            _missingProductKeys = [];
            return;
        }

        var products = await productRepository.GetProductsByKeysAsync(volumeFilteredKeys, ct);
        _underlyingProducts = products
            .Select(p => new UnderlyingProductInfo(
                p.ItemId,
                p.ItemFriendlyName,
                p.SkinUrl,
                p.ItemTier,
                (p.BidUnitPrice + p.AskUnitPrice) / 2,
                p.OrderMetaTotalWeekVolume
            ))
            .ToList();

        var literalKeys = _index.ProductKeys
            .Where(k => !k.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
            .ToHashSet();
        _missingProductKeys = literalKeys
            .Where(k => !volumeFilteredKeys.Contains(k))
            .ToList();
    }

    private Task OnIntervalChangedAsync(CandleInterval interval)
    {
        _selectedInterval = interval;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private sealed record UnderlyingProductInfo(
        string ProductKey,
        string FriendlyName,
        string? SkinUrl,
        ItemTier ProductTier,
        double CurrentPrice,
        double WeekVolume
    );
}
