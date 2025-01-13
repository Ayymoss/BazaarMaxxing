using BazaarCompanion.Interfaces;
using BazaarCompanion.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Refit;
using Spectre.Console;

namespace BazaarCompanion;

public static class Program
{
    public static async Task Main()
    {
        while (Console.WindowWidth < 200)
        {
            AnsiConsole.Clear();
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Warning: The console window is narrow. Please enlarge the window to at least 200.[/]");
            AnsiConsole.MarkupLine($"[green]The console is currently {Console.WindowWidth} wide.[/]");
            await Task.Delay(100);
        }

        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("_Configuration.json", false, true);

        builder.Services.Configure<Configuration>(builder.Configuration);
        var configuration = builder.Configuration.Get<Configuration>() ?? new Configuration();

        builder.Services.AddRefitClient<IHyPixelApi>().ConfigureHttpClient(x =>
        {
            x.DefaultRequestHeaders.Add("API-Key", configuration.HyPixelApikey);
            x.BaseAddress = new Uri("https://api.hypixel.net/");
        });

        builder.Services.AddSingleton<BazaarDisplay>();
        builder.Services.AddHostedService<AppEntry>();

        var app = builder.Build();
        await app.RunAsync();
    }
}
