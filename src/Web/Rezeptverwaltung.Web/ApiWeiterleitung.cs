using System.Net;
using Microsoft.AspNetCore.Authentication;
using Yarp.ReverseProxy.Forwarder;

namespace Rezeptverwaltung.Web;

/// <summary>
/// Leitet <c>/api/…</c> aus dem Browser an die API weiter und setzt dabei den Access Token der Sitzung als Bearer
/// (ADR 0016). Das Sitzungs-Cookie verlässt den Server nie Richtung API.
/// </summary>
internal static class ApiWeiterleitung
{
    public const long MaximaleAnfrageBytes = 1_000_000;

    /// <summary>
    /// Pflicht-Header für alle programmatischen Aufrufe. Eine fremde Seite kann ihn nur mit CORS-Vorabprüfung setzen,
    /// die hier nie erlaubt wird; zusammen mit SameSite=Lax schützt das vor CSRF.
    /// </summary>
    public const string CsrfHeader = "X-CSRF";

    public static IServiceCollection AddApiWeiterleitung(this IServiceCollection services, IConfiguration konfiguration)
    {
        var adresse = konfiguration["Api:Adresse"];
        if (string.IsNullOrWhiteSpace(adresse) || !Uri.TryCreate(adresse, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "Api:Adresse (z. B. http://api:8080) muss gesetzt sein. Lokal über den AppHost starten: "
                + "dotnet run --project src/Host/Rezeptverwaltung.AppHost");
        }

        services.AddHttpForwarder();
        services.AddSingleton(new ApiZiel(adresse));
        // Austauschbar für Tests (In-Memory-API); im Betrieb eine eigene Verbindung ohne Cookies und Proxy.
        services.AddSingleton(_ => new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        }));
        return services;
    }

    public static IEndpointRouteBuilder MapApiWeiterleitung(this IEndpointRouteBuilder endpoints)
    {
        var ziel = endpoints.ServiceProvider.GetRequiredService<ApiZiel>();
        var verbindung = endpoints.ServiceProvider.GetRequiredService<HttpMessageInvoker>();
        endpoints.MapForwarder("/api/{**rest}", ziel.Adresse, ForwarderRequestConfig.Empty, new MitAccessToken(), verbindung)
            .RequireAuthorization();
        return endpoints;
    }

    /// <summary>Verlangt <see cref="CsrfHeader"/> bei <c>/api</c> und <c>/bff</c>; ohne ihn 403.</summary>
    public static IApplicationBuilder UseCsrfSchutz(this IApplicationBuilder app) =>
        app.Use(async (kontext, weiter) =>
        {
            if (Anmeldung.IstProgrammatisch(kontext.Request) && kontext.Request.Headers[CsrfHeader] != "1")
            {
                kontext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await weiter(kontext).ConfigureAwait(false);
        });

    internal sealed record ApiZiel(string Adresse);

    private sealed class MitAccessToken : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(
            HttpContext httpContext,
            HttpRequestMessage proxyRequest,
            string destinationPrefix,
            CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken).ConfigureAwait(false);

            // Nur der Token geht an die API, nicht das Sitzungs-Cookie oder Header des Browsers zur Anmeldung.
            proxyRequest.Headers.Remove("Cookie");
            proxyRequest.Headers.Remove(CsrfHeader);
            var token = await httpContext.GetTokenAsync("access_token").ConfigureAwait(false);
            proxyRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }
}
