using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Api;

/// <summary>
/// Anmeldung über den Identity Provider (ADR 0004/0015). Die API ist reiner Resource Server: Sie prüft Signatur,
/// Aussteller, Zielgruppe und Gültigkeit des Tokens und kennt keine Passwörter.
/// </summary>
internal static class Anmeldung
{
    public static IServiceCollection AddAnmeldung(this IServiceCollection services, IConfiguration konfiguration, IHostEnvironment umgebung)
    {
        var abschnitt = konfiguration.GetSection("Authentifizierung");
        var authority = abschnitt["Authority"];
        var audience = abschnitt["Audience"];
        var nurHttps = abschnitt.GetValue("RequireHttpsMetadata", defaultValue: true);

        if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException(
                "Authentifizierung:Authority und Authentifizierung:Audience müssen gesetzt sein. Lokal über den AppHost starten: "
                + "dotnet run --project src/Host/Rezeptverwaltung.AppHost");
        }

        if (!nurHttps && umgebung.IsProduction())
        {
            throw new InvalidOperationException("Authentifizierung:RequireHttpsMetadata=false ist in Produktion nicht erlaubt.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(optionen =>
            {
                optionen.Authority = authority;
                optionen.Audience = audience;
                optionen.RequireHttpsMetadata = nurHttps;
                optionen.MapInboundClaims = false; // Claim-Namen wie im Token (Rollen-Claim von Zitadel)
                optionen.TokenValidationParameters.ValidateIssuer = true;
                optionen.TokenValidationParameters.ValidateAudience = true;
                optionen.TokenValidationParameters.ValidateLifetime = true;
                optionen.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(1);
            });

        // Sicher als Voreinstellung: Ohne ausdrückliches AllowAnonymous verlangt jeder Endpunkt eine Anmeldung.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddHttpContextAccessor();
        services.AddScoped<IAktuellerBenutzer, TokenBenutzer>();
        return services;
    }
}
