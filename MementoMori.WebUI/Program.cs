using System.Globalization;
using MementoMori;
using MementoMori.Common;
using MementoMori.Jobs;
using MementoMori.Option;
using MementoMori.WebUI.ViewModels;
using MudBlazor.Services;
using MementoMori.WebUI.Extensions;
using Quartz;
using ReactiveUI;
using MementoMori.WebUI;
using MementoMori.WebUI.UI;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Index = MementoMori.BlazorShared.Pages.Index;
using Ortega.Common.Manager;
using MudBlazor;
using MagicOnion;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.DataProtection;

internal class Program
{
    public static void Main(string[] args)
    {
        if (args is ["--healthcheck", var healthUrl])
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            try
            {
                using var response = client.GetAsync(healthUrl).GetAwaiter().GetResult();
                Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
            }
            catch (HttpRequestException) { Environment.ExitCode = 1; }
            catch (OperationCanceledException) { Environment.ExitCode = 1; }
            return;
        }
        PlatformRegistrationManager.SetRegistrationNamespaces(RegistrationNamespace.Blazor);
        var builder = WebApplication.CreateBuilder(args);

        var configDirectory = Path.GetFullPath(builder.Configuration["ConfigDirectory"] ?? Directory.GetCurrentDirectory());
        Directory.CreateDirectory(configDirectory);
        IFileProvider physicalProvider = new PhysicalFileProvider(configDirectory);
        builder.Services.AddSingleton(physicalProvider);
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(configDirectory, "DataProtection-Keys")));

        builder.Configuration.AddJsonFile(physicalProvider, "appsettings.other.json", true, true);
        builder.Configuration.AddJsonFile(physicalProvider, "appsettings.user.json", true, true);

        builder.Services.AddMudServices();
        builder.Services.AddMudMarkdownServices();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddMementoMori();
        builder.Services.AddMementoMoriBlazorShared();
        builder.Services.AddMementoMoriWebUI();
        builder.Services.AddHttpClient();

        builder.Services.AddOptions();
        builder.Services.ConfigureWritable<AuthOption>(builder.Configuration.GetSection("AuthOption"), "appsettings.user.json");
        builder.Services.ConfigureWritable<GameConfig>(builder.Configuration.GetSection("GameConfig"), "appsettings.user.json");
        builder.Services.ConfigureWritable<PlayersOption>(builder.Configuration.GetSection("PlayersOption"), "appsettings.user.json");
        builder.Services.Configure<StaticFileOptions>(opt =>
        {
            opt.HttpsCompression = HttpsCompressionMode.Compress;
            opt.OnPrepareResponse = ctx =>
            {
                var typedHeaders = ctx.Context.Response.GetTypedHeaders();
                typedHeaders.CacheControl = new CacheControlHeaderValue()
                {
                    Public = true,
                    MaxAge = TimeSpan.FromDays(1)
                };
            };
        });

        builder.Services.AddQuartz(q =>
        {
            var key = new JobKey(nameof(AutoLoginJob));
            q.AddJob<AutoLoginJob>(j => j.WithIdentity(key));
            q.AddTrigger(t => t.ForJob(key).WithIdentity(nameof(AutoLoginJob))
                .StartNow().WithSimpleSchedule(s => s.WithIntervalInMinutes(1).RepeatForever()));
        });
        builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
        var app = builder.Build();
        Services.Setup(app.Services);

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");

        app.UseStaticFiles();
        app.MapStaticAssets();
        app.UseAntiforgery();
        app.MapGet("/healthz", () => Results.Ok(new { status = "ready" }));
        app.MapRazorComponents<App>()
            .AddAdditionalAssemblies(typeof(Index).Assembly)
            .AddInteractiveServerRenderMode();
        //app.UseRouting();

        //app.MapBlazorHub();
        //app.MapFallbackToPage("/_Host");

        InitializeAsync(app.Services).ConfigureAwait(false).GetAwaiter().GetResult();
        app.Run();
    }

    private static async Task InitializeAsync(IServiceProvider sp)
    {
        var accountManager = sp.GetRequiredService<AccountManager>();
        var logger = sp.GetRequiredService<ILogger<Program>>();
        var networkManager = sp.GetRequiredService<MementoNetworkManager>();
        accountManager.MigrateToAccountArray();
        accountManager.CurrentCulture = CultureInfo.CurrentCulture;
        try
        {
            await networkManager.Initialize();
            await networkManager.DownloadMasterCatalog();
        }
        catch (Exception e) when (MementoNetworkManager.HasUsableMasterData())
        {
            logger.LogWarning(e, "Failed to update master data; using existing files");
        }
        networkManager.SetCultureInfo(CultureInfo.CurrentCulture);
    }
}
