using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Rezeptverwaltung.Web;

/// <summary>
/// Anmeldung nach dem BFF-Muster (ADR 0016): Der Server meldet per OIDC (Authorization Code mit PKCE, vertraulicher
/// Client) beim Identity Provider an und hält die Tokens in einem verschlüsselten, HttpOnly-Sitzungs-Cookie.
/// Im Browser liegt nie ein Token.
/// </summary>
internal static class Anmeldung
{
    /// <summary>Name des Sitzungs-Cookies.</summary>
    public const string CookieName = "rezeptverwaltung.sitzung";

    /// <summary>Wie lange eine Sitzung ohne Benutzung gültig bleibt (gleitend). Das Tablet in der Backstube bleibt angemeldet.</summary>
    public static readonly TimeSpan Sitzungsdauer = TimeSpan.FromDays(14);

    /// <summary>offline_access liefert den Refresh Token, mit dem die Sitzung ohne neue Anmeldung weiterläuft.</summary>
    private static readonly string[] StandardScopes = ["openid", "profile", "offline_access"];

    public static IServiceCollection AddWebAnmeldung(this IServiceCollection services, IConfiguration konfiguration, IHostEnvironment umgebung)
    {
        var abschnitt = konfiguration.GetSection("Anmeldung");
        var authority = abschnitt["Authority"];
        var clientId = abschnitt["ClientId"];
        var clientSecret = abschnitt["ClientSecret"];
        var nurHttps = abschnitt.GetValue("RequireHttpsMetadata", defaultValue: true);
        var zusatzScopes = (abschnitt["Scopes"] ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Anmeldung:Authority, Anmeldung:ClientId und Anmeldung:ClientSecret müssen gesetzt sein. Lokal über den AppHost starten: "
                + "dotnet run --project src/Host/Rezeptverwaltung.AppHost");
        }

        if (!nurHttps && umgebung.IsProduction())
        {
            throw new InvalidOperationException("Anmeldung:RequireHttpsMetadata=false ist in Produktion nicht erlaubt.");
        }

        services.AddSingleton<TokenErneuerung>();
        var sichereCookies = umgebung.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        services.AddAntiforgery(optionen => optionen.Cookie.SecurePolicy = sichereCookies);
        services
            .AddAuthentication(optionen =>
            {
                optionen.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                optionen.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(optionen =>
            {
                optionen.Cookie.Name = CookieName;
                optionen.Cookie.HttpOnly = true;
                optionen.Cookie.SameSite = SameSiteMode.Lax; // Strict verlöre das Cookie direkt nach der Rückleitung vom Login.
                // Außerhalb der Entwicklung immer Secure, auch falls hinter dem TLS-Proxy die Forwarded Headers fehlen.
                optionen.Cookie.SecurePolicy = sichereCookies;
                optionen.ExpireTimeSpan = Sitzungsdauer;
                optionen.SlidingExpiration = true;
                optionen.Events.OnValidatePrincipal = kontext =>
                    kontext.HttpContext.RequestServices.GetRequiredService<TokenErneuerung>().PruefenAsync(kontext);
                optionen.Events.OnRedirectToAccessDenied = kontext =>
                {
                    kontext.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(optionen =>
            {
                optionen.Authority = authority;
                optionen.ClientId = clientId;
                optionen.ClientSecret = clientSecret;
                optionen.RequireHttpsMetadata = nurHttps;
                optionen.ResponseType = OpenIdConnectResponseType.Code;
                optionen.UsePkce = true;
                optionen.SaveTokens = true;
                optionen.MapInboundClaims = false;
                optionen.GetClaimsFromUserInfoEndpoint = false;
                optionen.TokenValidationParameters.NameClaimType = "name";
                optionen.Scope.Clear();
                foreach (var scope in StandardScopes.Concat(zusatzScopes))
                {
                    optionen.Scope.Add(scope);
                }

                optionen.SignedOutRedirectUri = "/";
                optionen.Events.OnRedirectToIdentityProvider = kontext =>
                {
                    // Programmatische Aufrufe bekommen 401 statt einer Weiterleitung, die der Browser nicht sinnvoll folgen kann.
                    if (IstProgrammatisch(kontext.Request))
                    {
                        kontext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        kontext.HandleResponse();
                    }

                    return Task.CompletedTask;
                };
                optionen.Events.OnTicketReceived = kontext =>
                {
                    // Die Sitzung überlebt einen Neustart des Browsers bzw. Tablets.
                    kontext.Properties!.IsPersistent = true;
                    return Task.CompletedTask;
                };
            });

        // Sicher als Voreinstellung: Ohne ausdrückliches AllowAnonymous verlangt jeder Endpunkt eine Anmeldung.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    public static IEndpointRouteBuilder MapAnmeldung(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/anmelden", (string? returnUrl) =>
            TypedResults.Challenge(new AuthenticationProperties { RedirectUri = NurLokal(returnUrl) }))
            .AllowAnonymous();

        endpoints.MapPost("/abmelden", async (HttpContext kontext, IAntiforgery antiforgery) =>
        {
            // Formular-POST aus der App mit Antiforgery-Token: Eine fremde Seite kann niemanden abmelden.
            if (!await antiforgery.IsRequestValidAsync(kontext).ConfigureAwait(false))
            {
                return Results.BadRequest();
            }

            if (kontext.User.Identity?.IsAuthenticated != true)
            {
                return Results.Redirect("/");
            }

            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        })
            .AllowAnonymous()
            .DisableAntiforgery(); // wird oben selbst geprüft, damit ein Fehler 400 statt einer Ausnahme ergibt

        endpoints.MapGet("/bff/benutzer", (ClaimsPrincipal benutzer, HttpContext kontext, IAntiforgery antiforgery) =>
            TypedResults.Ok(new BenutzerAntwort(
                benutzer.FindFirstValue("name") ?? benutzer.FindFirstValue("preferred_username") ?? benutzer.FindFirstValue("sub") ?? "?",
                antiforgery.GetAndStoreTokens(kontext).RequestToken!)));

        return endpoints;
    }

    /// <summary>Nur relative Pfade dieser Anwendung, damit niemand über den Login auf eine fremde Seite umleitet.</summary>
    internal static string NurLokal(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl[0] == '/' && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
            && !returnUrl.Any(char.IsControl)
            ? returnUrl
            : "/";

    internal static bool IstProgrammatisch(HttpRequest anfrage) =>
        anfrage.Path.StartsWithSegments("/api") || anfrage.Path.StartsWithSegments("/bff");

    internal sealed record BenutzerAntwort(string Name, string AbmeldeToken);
}
