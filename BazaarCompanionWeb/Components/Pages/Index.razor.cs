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
    private CandleInterval _selectedInterval = CandleInterval.FifteenMinute;

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

        List<UnderlyingProductInfo> products = [];
        List<string> missing = [];

        foreach (var productKey in _index.ProductKeys)
        {
            try
            {
                var product = await productRepository.GetProductAsync(productKey, ct);
                if (product is not null)
                {
                    products.Add(new UnderlyingProductInfo(
                        productKey,
                        product.ItemFriendlyName,
                        product.SkinUrl,
                        product.ItemTier,
                        (product.BidUnitPrice + product.AskUnitPrice) / 2
                    ));
                }
                else
                {
                    missing.Add(productKey);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not load product info for {ProductKey}", productKey);
                missing.Add(productKey);
            }
        }

        _underlyingProducts = products;
        _missingProductKeys = missing;
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
        double CurrentPrice
    );
}
