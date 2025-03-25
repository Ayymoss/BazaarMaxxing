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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Radzen;
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

#if DEBUG
        builder.WebHost.ConfigureKestrel(options => { options.ListenLocalhost(8123); });
#else
        builder.WebHost.UseKestrel(options =>
        {
            options.ListenAnyIP(6969);
        });
#endif

        builder.Services.AddDbContextFactory<DataContext>(options =>
        {
#if DEBUG
            options.UseSqlite($"Data Source={Path.Join(AppContext.BaseDirectory, "_Database", "Database.db")}",
#else
            options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"),
            //options.UseSqlite($"Data Source={Path.Join(AppContext.BaseDirectory, "_Database", "Database.db")}",
#endif
                sqlOpt => sqlOpt.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
        });

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddDataProtection()
            .SetApplicationName("BazaarWeb")
            .PersistKeysToFileSystem(new DirectoryInfo("/app/data/"));

        RegisterCustomServices(builder);
        RegisterPackageServices(builder);
        RegisterLogging();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var dbContext = services.GetRequiredService<DataContext>();
            dbContext.Database.Migrate();
        }

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.Services.GetRequiredService<ScheduledTaskRunner>().StartTimer();

        //app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
        Log.CloseAndFlush();
    }

    private static void RegisterLogging()
    {
#if DEBUG
        var logBaseDirectory = Path.Join(AppContext.BaseDirectory, "_Log");
        if (!Directory.Exists(logBaseDirectory))
            Directory.CreateDirectory(logBaseDirectory);
#else
        var logBaseDirectory = Path.Join("data", "_Log");
        if (!Directory.Exists(logBaseDirectory))
            Directory.CreateDirectory(logBaseDirectory);
#endif


        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Information()
            .MinimumLevel.Override("BazaarCompanionWeb", LogEventLevel.Debug)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
#else
            .MinimumLevel.Warning()
            .MinimumLevel.Override("BazaarCompanionWeb", LogEventLevel.Information)
#endif
            .Enrich.FromLogContext()
            .Enrich.With<ShortSourceContextEnricher>()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Join(logBaseDirectory, "bc-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 10,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{ShortSourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
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
        builder.Services.AddRadzenComponents();
    }

    private static void RegisterCustomServices(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ScheduledTaskRunner>();
        builder.Services.AddSingleton<TimeCache>();

        builder.Services.AddScoped<HyPixelService>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<IResourceQueryHelper<ProductPagination, ProductDataInfo>, ProductsPaginationQueryHelper>();
        
        builder.Services.AddAutoMapper(typeof(ProductProfile));
    }
}
