using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using Runnatics.Configuration;
using Runnatics.Hubs;
using Runnatics.Models.Client.Configuration;
using Runnatics.Services;
using BibMappingHub = Runnatics.Hubs.BibMappingHub;

namespace Runnatics.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddRfidOnlineIntegration(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<R700Settings>(
            configuration.GetSection("R700Settings"));

        services.AddHttpClient("ImpinjR700", (sp, client) =>
        {
            var settings = configuration
                .GetSection("R700Settings").Get<R700Settings>() ?? new();

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);

            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // R700 uses self-signed certs by default.
            // In production, install proper certs and tighten this.
            ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;
                if (errors == SslPolicyErrors.RemoteCertificateChainErrors)
                    return true; // Self-signed
                return false;
            }
        });

        services.AddScoped<R700CommunicationService>();
        services.AddScoped<RaceReaderService>();
        services.AddScoped<OnlineTagIngestionService>();
        services.AddSignalR();

        return services;
    }

    public static WebApplication MapRfidHubs(this WebApplication app)
    {
        app.MapHub<RaceHub>("/hubs/race").RequireCors("SignalR");
        app.MapHub<BibMappingHub>("/hubs/bib-mapping").RequireCors("SignalR");
        return app;
    }
}
