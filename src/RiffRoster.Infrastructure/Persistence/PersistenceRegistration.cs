using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RiffRoster;

public static class PersistenceRegistration
{
    public static IServiceCollection AddRiffRosterPersistence(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var provider = configuration["RIFFROSTER_STORAGE"] ?? "Json";
        if (provider.Equals("Json", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IStatePersistence, JsonStatePersistence>();
        else if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            var connection = configuration["RIFFROSTER_POSTGRES_CONNECTION"];
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException("RIFFROSTER_POSTGRES_CONNECTION is required for PostgreSQL storage.");
            services.AddSingleton<IStatePersistence>(_ => new PostgresStatePersistence(connection));
        }
        else throw new InvalidOperationException("RIFFROSTER_STORAGE must be Json or Postgres.");
        return services;
    }
}
