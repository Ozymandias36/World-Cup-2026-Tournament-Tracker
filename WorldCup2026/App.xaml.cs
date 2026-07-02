using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using WorldCup2026.Services;
using WorldCup2026.ViewModels;
using WorldCup2026.Views;

namespace WorldCup2026;

public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpClient();

        // 1. Local data — baseline structure (team names, UTC offsets, fallback)
        services.AddSingleton<LocalDataService>();
        services.AddTransient<IDataService>(sp => sp.GetRequiredService<LocalDataService>());

        // 2. Live scores API (matching IDs with local data)
        services.AddHttpClient<WorldCup26IrService>(client =>
        {
            client.BaseAddress = new Uri("https://worldcup26.ir");
            client.DefaultRequestHeaders.Add("User-Agent", "WorldCup2026-App/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(8);
        });
        services.AddTransient<IDataService>(sp => sp.GetRequiredService<WorldCup26IrService>());

        // 3. FIFA Official API
        services.AddHttpClient<FifaApiService>(client =>
        {
            client.BaseAddress = new Uri("https://api.fifa.com");
            client.DefaultRequestHeaders.Add("User-Agent", "WorldCup2026-App/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddTransient<IDataService>(sp => sp.GetRequiredService<FifaApiService>());

        // Aggregator merges data from all sources
        services.AddSingleton<DataServiceAggregator>();

        // PDF export
        services.AddSingleton<PdfExportService>();

        // ViewModels
        services.AddTransient<BracketViewModel>();
        services.AddTransient<GroupStageViewModel>();

        // MainWindow
        services.AddTransient<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    public T GetService<T>() where T : notnull => _serviceProvider.GetRequiredService<T>();
}
