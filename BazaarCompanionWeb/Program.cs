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
using Microsoft.EntityFrameworkCore;
using Radzen;
using Refit;
using Serilog;
using Serilog.Events;

namespace BazaarCompanionWeb;

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
            options.UseSqlite($"Data Source={Path.Join(AppContext.BaseDirectory, "_Database", "Database.db")}");
        });

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        RegisterCustomServices(builder);
        RegisterPackageServices(builder);
        RegisterLogging();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.Services.GetRequiredService<ScheduledTaskRunner>().StartTimer();

        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
        Log.CloseAndFlush();
    }

    private static void RegisterLogging()
    {
        if (!Directory.Exists(Path.Join(AppContext.BaseDirectory, "_Log")))
            Directory.CreateDirectory(Path.Join(AppContext.BaseDirectory, "_Log"));

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
                Path.Join(AppContext.BaseDirectory, "_Log", "bsb-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 10,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{ShortSourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void RegisterPackageServices(WebApplicationBuilder builder)
    {
        builder.Configuration.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("_Configuration.json", false, true);

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

        builder.Services.AddScoped<HyPixelService>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<IResourceQueryHelper<ProductPagination, ProductDataInfo>, ProductsPaginationQueryHelper>();
    }
}
