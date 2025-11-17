using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Runnatics.Services;
using Runnatics.Services.Interface;
namespace Runnatics.Services
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddEncryptionService(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));

            services.AddSingleton<IEncryptionService>(provider =>
            {
                var config = configuration.GetSection(EncryptionOptions.SectionName).Get<EncryptionOptions>();

                if (config == null || string.IsNullOrWhiteSpace(config.Key))
                    throw new InvalidOperationException("Encryption key is not configured. Add 'Encryption:Key' to your configuration.");

                return new EncryptionService(config.Key);
            });

            return services;
        }
    }
}