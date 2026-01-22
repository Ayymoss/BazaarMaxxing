using System.Runtime.InteropServices;
using System.Text.Json;
using BazaarCompanionWeb.Components;
using BazaarCompanionWeb.Context;
using BazaarCompanionWeb.Dtos;
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
using Refit;
using Serilog;
using Serilog.Events;

namespace BazaarCompanionWeb;

// TODO: Fire sale icon for items which have jumped price massively. 
public class Program
{
    public static void Main(string[] args)
    {
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
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                // Fallback to default path in data directory
                var dataDirectory = GetDataDirectory(builder.Environment);
                var dbPath = Path.Join(dataDirectory, "Database.db");
                connectionString = $"Data Source={dbPath}";
            }
            else
            {
                // Normalize the connection string to use absolute paths
                connectionString = NormalizeConnectionString(connectionString, builder.Environment);
            }
            
            // Ensure the database directory exists
            EnsureDatabaseDirectoryFromConnectionString(connectionString);
            
            options.UseSqlite(connectionString,
                sqlOpt => sqlOpt.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
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

        RegisterCustomServices(builder);
        RegisterPackageServices(builder);
        RegisterLogging(builder.Environment, dataDirectory);

        var app = builder.Build();

        // Ensure database directory exists before attempting migration
        EnsureDatabaseDirectoryExists(app.Configuration, app.Environment);

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

    private static string NormalizeConnectionString(string connectionString, IWebHostEnvironment environment)
    {
        // Parse the connection string to extract the database file path
        // Format: "Data Source=path/to/Database.db" or "Data Source=path/to/Database.db;Mode=ReadWrite"
        var dataSourceKey = "Data Source=";
        var dataSourceIndex = connectionString.IndexOf(dataSourceKey, StringComparison.OrdinalIgnoreCase);
        if (dataSourceIndex < 0)
            return connectionString; // Not a SQLite connection string, return as-is

        var startIndex = dataSourceIndex + dataSourceKey.Length;
        var endIndex = connectionString.IndexOf(';', startIndex);
        var dbPath = endIndex >= 0 
            ? connectionString.Substring(startIndex, endIndex - startIndex).Trim()
            : connectionString.Substring(startIndex).Trim();
        
        // Remove quotes if present
        if ((dbPath.StartsWith('"') && dbPath.EndsWith('"')) || 
            (dbPath.StartsWith('\'') && dbPath.EndsWith('\'')))
        {
            dbPath = dbPath.Substring(1, dbPath.Length - 2);
        }
        
        // Convert relative paths to absolute paths
        if (dbPath.StartsWith("./") || dbPath.StartsWith(".\\"))
        {
            dbPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, dbPath.Substring(2)));
        }
        else if (!Path.IsPathRooted(dbPath))
        {
            // Relative path without ./ prefix
            dbPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, dbPath));
        }
        else
        {
            // Already absolute, but normalize it
            dbPath = Path.GetFullPath(dbPath);
        }

        // Reconstruct the connection string with the normalized path
        var prefix = connectionString.Substring(0, startIndex);
        var suffix = endIndex >= 0 ? connectionString.Substring(endIndex) : string.Empty;
        return $"{prefix}{dbPath}{suffix}";
    }

    private static void EnsureDatabaseDirectoryFromConnectionString(string connectionString)
    {
        try
        {
            // Parse the connection string to extract the database file path
            var dataSourceKey = "Data Source=";
            var dataSourceIndex = connectionString.IndexOf(dataSourceKey, StringComparison.OrdinalIgnoreCase);
            if (dataSourceIndex < 0)
                return;

            var startIndex = dataSourceIndex + dataSourceKey.Length;
            var endIndex = connectionString.IndexOf(';', startIndex);
            var dbPath = endIndex >= 0 
                ? connectionString.Substring(startIndex, endIndex - startIndex).Trim()
                : connectionString.Substring(startIndex).Trim();
            
            // Remove quotes if present
            if ((dbPath.StartsWith('"') && dbPath.EndsWith('"')) || 
                (dbPath.StartsWith('\'') && dbPath.EndsWith('\'')))
            {
                dbPath = dbPath.Substring(1, dbPath.Length - 2);
            }

            // Normalize to absolute path
            dbPath = Path.GetFullPath(dbPath);

            var dbDirectory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
                Log.Information("Created database directory: {Directory}", dbDirectory);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to ensure database directory exists. Database operations may fail.");
        }
    }

    private static void EnsureDatabaseDirectoryExists(IConfiguration configuration, IWebHostEnvironment environment)
    {
        try
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                // Fallback to default path in data directory
                var dataDirectory = GetDataDirectory(environment);
                var dbPath = Path.Join(dataDirectory, "Database.db");
                connectionString = $"Data Source={dbPath}";
            }
            else
            {
                // Normalize the connection string
                connectionString = NormalizeConnectionString(connectionString, environment);
            }

            EnsureDatabaseDirectoryFromConnectionString(connectionString);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to ensure database directory exists. Database operations may fail.");
        }
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
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning);
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
        builder.Services.AddScoped<TechnicalAnalysisService>();
        builder.Services.AddScoped<OrderBookAnalysisService>();
        builder.Services.AddScoped<BrowserStorage>();
        builder.Services.AddSingleton<ComparisonStateService>();

        builder.Services.AddHostedService<OhlcAggregationService>();
    }
}
