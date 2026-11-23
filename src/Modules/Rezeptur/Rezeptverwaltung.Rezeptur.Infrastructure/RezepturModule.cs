using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Http;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

namespace Rezeptverwaltung.Rezeptur.Infrastructure;

/// <summary>Einziger öffentlicher Einstiegspunkt des Moduls „Rezeptur“ für den Host.</summary>
public static class RezepturModule
{
    public static IServiceCollection AddRezepturModule(this IServiceCollection services, string verbindungszeichenfolge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verbindungszeichenfolge);

        services.AddDbContext<RezepturDbContext>(optionen => RezepturDbContext.Konfiguriere(optionen, verbindungszeichenfolge));
        services.AddHealthChecks().AddDbContextCheck<RezepturDbContext>("rezeptur-datenbank");
        services.AddScoped<IRezeptRepository, EfRezeptRepository>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<RezeptAnlegenHandler>();
        services.AddScoped<RezeptAbfragenHandler>();
        services.AddScoped<RezeptSkalierenHandler>();
        services.AddScoped<RezeptAufFormSkalierenHandler>();
        services.AddScoped<FassungAbfragenHandler>();
        services.AddScoped<EntwurfAnlegenHandler>();
        services.AddScoped<EntwurfBearbeitenHandler>();
        services.AddScoped<EntwurfFreigebenHandler>();
        services.AddScoped<EntwurfVerwerfenHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapRezepturModule(this IEndpointRouteBuilder endpoints) =>
        RezepturEndpoints.MapRezeptur(endpoints);

    /// <summary>Bringt das Datenbankschema des Moduls auf den neuesten Stand (ADR 0011).</summary>
    public static async Task MigriereRezepturDatenbankAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var datenbank = scope.ServiceProvider.GetRequiredService<RezepturDbContext>();
            await datenbank.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
