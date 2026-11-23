using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure.Http;
using Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

namespace Rezeptverwaltung.Rezeptur.Infrastructure;

/// <summary>Einziger öffentlicher Einstiegspunkt des Moduls „Rezeptur“ für den Host.</summary>
public static class RezepturModule
{
    public static IServiceCollection AddRezepturModule(this IServiceCollection services)
    {
        services.AddSingleton<IRezeptRepository, InMemoryRezeptRepository>();
        services.AddScoped<RezeptAnlegenHandler>();
        services.AddScoped<RezeptAbfragenHandler>();
        services.AddScoped<RezeptSkalierenHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapRezepturModule(this IEndpointRouteBuilder endpoints) =>
        RezepturEndpoints.MapRezeptur(endpoints);
}
