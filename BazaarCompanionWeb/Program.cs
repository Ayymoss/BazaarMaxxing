using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using BazaarCompanionWeb.Components;
using BazaarCompanionWeb.Configurations;
using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
using BazaarCompanionWeb.Entities;
using BazaarCompanionWeb.Interfaces;
using BazaarCompanionWeb.Interfaces.Api;
using BazaarCompanionWeb.Interfaces.Database;
using BazaarCompanionWeb.Models.Pagination.MetaPaginations;
using BazaarCompanionWeb.Queries;
using BazaarCompanionWeb.Repositories;
using BazaarCompanionWeb.Services;
using BazaarCompanionWeb.Utilities;
using BazaarCompanionWeb.Hubs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Refit;
using Serilog;
using Serilog.Events;

namespace BazaarCompanionWeb;

// TODO: Fire sale icon for items which have jumped price massively. 
public class Program
{
    public static void Main(string[] args)
    {
        // Set default culture to en-US for consistent currency formatting ($ instead of ¤)
        // This is required for Docker containers that may not have full ICU globalization data
        var culture = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var builder = WebApplication.CreateBuilder(args);

        // Configure Kestrel based on environment
        if (builder.Environment.IsDevelopment())
        {
            builder.WebHost.ConfigureKestrel(options => { options.ListenLocalhost(8123); });
        }
        else
        {
            builder.WebHost.UseKestrel(options =>
            {
                options.ListenAnyIP(6969);
            });
        }

        builder.Services.AddDbContextFactory<DataContext>(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is required. Configure it in appsettings or via ConnectionStrings__DefaultConnection env var.");
            options.UseNpgsql(connectionString,
                npgsqlOpt => npgsqlOpt.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
        });

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddSignalR();

        var dataDirectory = GetDataDirectory(builder.Environment);
        var dataProtectionKeysPath = Path.Join(dataDirectory, "DataProtection-Keys");
        if (!Directory.Exists(dataProtectionKeysPath))
            Directory.CreateDirectory(dataProtectionKeysPath);

        builder.Services.AddDataProtection()
            .SetApplicationName("BazaarWeb")
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

        // RegisterLogging must be called FIRST to set Log.Logger before UseSerilog() in RegisterPackageServices
        RegisterLogging(builder.Environment, dataDirectory);
        RegisterCustomServices(builder);
        RegisterPackageServices(builder);

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            try
            {
                var dbContext = services.GetRequiredService<DataContext>();
                dbContext.Database.Migrate();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to migrate database. Application will continue but database operations may fail.");
                // Don't throw - allow application to start even if migration fails
                // This is useful for debugging connection string issues
                if (app.Environment.IsDevelopment())
                {
                    throw; // Re-throw in development for easier debugging
                }
            }
        }

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.Services.GetRequiredService<ScheduledTaskRunner>().StartTimer();

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        //app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapHub<ProductHub>("/hubs/products");

        // Chart data API for lazy loading historical candles
        app.MapGet("/api/chart/{productKey}/{interval:int}", async (
            string productKey,
            int interval,
            long? before,
            int? limit,
            IOhlcRepository ohlcRepository,
            CancellationToken ct) =>
        {
            var candleInterval = (CandleInterval)interval;
            var dataLimit = Math.Min(limit ?? 200, 500); // Cap at 500 per request
            
            List<OhlcDataPoint> candles;
            if (before.HasValue)
            {
                // Load historical data before the specified timestamp
                var beforeTime = DateTimeOffset.FromUnixTimeMilliseconds(before.Value).UtcDateTime;
                candles = await ohlcRepository.GetCandlesBeforeAsync(productKey, candleInterval, beforeTime, dataLimit, ct);
            }
            else
            {
                // Initial load - get most recent candles
                candles = await ohlcRepository.GetCandlesAsync(productKey, candleInterval, dataLimit, ct);
            }
            
            // Return in KLineChart format (timestamp in milliseconds)
            var result = candles.Select(c => new
            {
                timestamp = new DateTimeOffset(c.Time).ToUnixTimeMilliseconds(),
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume,
                askClose = c.AskClose
            }).ToList();
            
            return Results.Ok(result);
        });

        // Index chart API for aggregated OHLC data (ETF-like indices)
        app.MapGet("/api/chart/index/{slug}/{interval:int}", async (
            string slug,
            int interval,
            int? limit,
            IndexAggregationService indexService,
            IOptions<List<IndexConfiguration>> indexOptions,
            CancellationToken ct) =>
        {
            // Check if index exists
            var indices = indexOptions.Value;
            var index = indices.FirstOrDefault(i => i.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
            if (index is null)
            {
                return Results.NotFound(new { error = $"Index '{slug}' not found" });
            }

            var candleInterval = (CandleInterval)interval;
            var dataLimit = Math.Min(limit ?? 200, 500);

            var candles = await indexService.GetAggregatedCandlesAsync(slug, candleInterval, dataLimit, ct);

            // Return in KLineChart format (timestamp in milliseconds)
            var result = candles.Select(c => new
            {
                timestamp = new DateTimeOffset(c.Time).ToUnixTimeMilliseconds(),
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume,
                askClose = c.AskClose
            }).ToList();

            return Results.Ok(result);
        });

        app.Run();
        Log.CloseAndFlush();
    }

    private static string GetDataDirectory(IWebHostEnvironment environment)
    {
        // Check for environment variable first
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir))
            return dataDir;

        // For development (typically Windows local debugging): use _Data subdirectory in base directory
        if (environment.IsDevelopment())
        {
            return Path.Join(AppContext.BaseDirectory, "_Data");
        }

        // For production (typically Linux Docker): use /app/data
        // Also check if we're running on Windows in production (unlikely but possible)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Join(AppContext.BaseDirectory, "_Data");
        }

        return "/app/data";
    }

    private static void RegisterLogging(IWebHostEnvironment environment, string dataDirectory)
    {
        var logBaseDirectory = Path.Join(dataDirectory, "_Log");
        if (!Directory.Exists(logBaseDirectory))
            Directory.CreateDirectory(logBaseDirectory);

        var loggerConfig = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.With<ShortSourceContextEnricher>()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Join(logBaseDirectory, "bc-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 10,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{ShortSourceContext}] {Message:lj}{NewLine}{Exception}");

        // Configure log levels based on environment
        if (environment.IsDevelopment())
        {
            loggerConfig
                .MinimumLevel.Information()
                .MinimumLevel.Override("BazaarCompanionWeb", LogEventLevel.Debug)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning);
        }
        else
        {
            loggerConfig
                .MinimumLevel.Warning()
                .MinimumLevel.Override("BazaarCompanionWeb", LogEventLevel.Information);
        }

        Log.Logger = loggerConfig.CreateLogger();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static void RegisterPackageServices(WebApplicationBuilder builder)
    {
        const string configFileName = "_Configuration.json";
        var configFilePath = Path.Combine(AppContext.BaseDirectory, configFileName);

        if (!File.Exists(configFilePath))
        {
            var defaultConfig = new Configuration();
            var defaultJson = JsonSerializer.Serialize(defaultConfig, JsonOptions);

            File.WriteAllText(configFilePath, defaultJson);
        }

        builder.Configuration.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(configFileName, false, true);

        builder.Services.Configure<Configuration>(builder.Configuration);
        var configuration = builder.Configuration.Get<Configuration>() ?? new Configuration();

        builder.Services.AddRefitClient<IHyPixelApi>().ConfigureHttpClient(x =>
        {
            x.DefaultRequestHeaders.Add("API-Key", configuration.HyPixelApikey);
            x.BaseAddress = new Uri("https://api.hypixel.net/");
        });

        builder.Host.UseSerilog();
        builder.Services.AddLogging();

        builder.Services.AddHttpClient("BMClient", options => options.DefaultRequestHeaders.UserAgent
            .ParseAdd("BazaarMaxxing/1.0.0"));
    }

    private static void RegisterCustomServices(IHostApplicationBuilder builder)
    {
        // Bind Indices configuration from appsettings.json
        builder.Services.Configure<List<IndexConfiguration>>(builder.Configuration.GetSection("Indices"));

        builder.Services.AddSingleton<ScheduledTaskRunner>();
        builder.Services.AddSingleton<TimeCache>();
        builder.Services.AddSingleton<LiveCandleTracker>();

        builder.Services.AddScoped<HyPixelService>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<IOhlcRepository, OhlcRepository>();
        builder.Services.AddScoped<IOpportunityScoringService, OpportunityScoringService>();
        builder.Services.AddScoped<IResourceQueryHelper<ProductPagination, ProductDataInfo>, ProductsPaginationQueryHelper>();
        builder.Services.AddSingleton<MarketAnalyticsService>();
        builder.Services.AddSingleton<MarketInsightsService>();
        builder.Services.AddScoped<OrderBookAnalysisService>();
        builder.Services.AddScoped<BrowserStorage>();
        builder.Services.AddSingleton<ComparisonStateService>();
        builder.Services.AddSingleton<AboutModalService>();
        builder.Services.AddScoped<IndexAggregationService>();

        builder.Services.AddHostedService<OhlcAggregationService>();
    }
}
