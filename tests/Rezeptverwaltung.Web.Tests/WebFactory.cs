extern alias WebHost;

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>
/// Startet die Weboberfläche (BFF) im Speicher, verbunden mit der echten API (im Speicher, echtes PostgreSQL) und dem
/// echten Login-Server (OIDC, ADR 0015/0016). Nichts am Login ist vorgetäuscht: Code, PKCE, Token und Cookie sind echt.
/// </summary>
public class WebFactory : WebApplicationFactory<WebHost::Program>, IAsyncLifetime
{
    /// <summary>Interne Adresse der API; die Anfragen gehen über <see cref="ApiMitschnitt"/> an die API im Speicher.</summary>
    public const string ApiAdresse = "http://api.intern";

    public ApiFactory Api { get; } = new();

    /// <summary>Zeit des Web-Servers; vorstellbar, um den Ablauf des Access Tokens zu erreichen.</summary>
    public VerstellbareZeit Zeit { get; } = new();

    /// <summary>Verbindung des Web-Servers zum Login-Server; kann die Erneuerung von Tokens scheitern lassen.</summary>
    public LoginVerbindung LoginVerbindung { get; } = new() { InnerHandler = new HttpClientHandler() };

    /// <summary>Umgebung des Web-Servers (Voreinstellung Development).</summary>
    protected virtual string Umgebung => "Development";

    /// <summary>Was zuletzt an die API weitergeleitet wurde.</summary>
    public ApiMitschnitt Mitschnitt { get; } = new();

    public virtual async ValueTask InitializeAsync()
    {
        await Api.InitializeAsync();
        Mitschnitt.InnerHandler = Api.Server.CreateHandler();
    }

    /// <summary>Client mit Cookies, ohne automatische Weiterleitung, über HTTPS (sichere Cookies wie im Betrieb).</summary>
    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    /// <summary>Meldet wie ein Browser an: Weiterleitung zum Login, Formular ausfüllen, Code zurück an /signin-oidc.</summary>
    public async Task<HttpClient> AngemeldetAlsAsync(Persona persona, string? anzeigename = null, string returnUrl = "/")
    {
        var browser = CreateBrowser();
        browser.DefaultRequestHeaders.Add("X-CSRF", "1");
        var ziel = await AnmeldenAsync(browser, persona, anzeigename, returnUrl);
        if (ziel != returnUrl)
        {
            throw new InvalidOperationException($"Nach dem Login ging es nach „{ziel}“ statt „{returnUrl}“.");
        }

        return browser;
    }

    /// <summary>Führt den Login durch und liefert, wohin die Weboberfläche danach weiterleitet.</summary>
    public static async Task<string> AnmeldenAsync(HttpClient browser, Persona persona, string? anzeigename = null, string returnUrl = "/")
    {
        ArgumentNullException.ThrowIfNull(browser);
        using var start = await browser.GetAsync($"/anmelden?returnUrl={Uri.EscapeDataString(returnUrl)}");
        if (start.StatusCode != HttpStatusCode.Found)
        {
            throw new InvalidOperationException($"/anmelden lieferte {start.StatusCode}.");
        }

        // Login-Formular des Login-Servers absenden (Person und Claims wie bei einer Anmeldung im Browser).
        using var loginServer = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var formular = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = persona.Name,
            ["claims"] = TestLogin.LoginClaims(persona, anzeigename),
        });
        using var login = await loginServer.PostAsync(start.Headers.Location, formular);
        var html = await login.Content.ReadAsStringAsync();

        // response_mode=form_post: Der Login-Server antwortet mit einem Formular, das der Browser an /signin-oidc schickt.
        var felder = Regex.Matches(html, "<input type=\"hidden\" name=\"(?<n>[^\"]+)\" value=\"(?<v>[^\"]*)\"")
            .ToDictionary(m => m.Groups["n"].Value, m => WebUtility.HtmlDecode(m.Groups["v"].Value));
        if (!felder.ContainsKey("code"))
        {
            throw new InvalidOperationException($"Kein Code vom Login-Server: {html}");
        }

        using var rueck = await browser.PostAsync("/signin-oidc", new FormUrlEncodedContent(felder));
        if (rueck.StatusCode != HttpStatusCode.Found)
        {
            throw new InvalidOperationException($"/signin-oidc lieferte {rueck.StatusCode}: {await rueck.Content.ReadAsStringAsync()}");
        }

        return rueck.Headers.Location!.OriginalString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Umgebung);
        builder.UseSetting("Api:Adresse", ApiAdresse);
        builder.UseSetting("Anmeldung:Authority", TestLogin.Authority);
        builder.UseSetting("Anmeldung:ClientId", TestLogin.WebClientId);
        builder.UseSetting("Anmeldung:ClientSecret", "geheim");
        builder.UseSetting("Anmeldung:RequireHttpsMetadata", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<HttpMessageInvoker>();
            services.AddSingleton(new HttpMessageInvoker(Mitschnitt, disposeHandler: false));
            services.Configure<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>(
                Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme,
                optionen => optionen.BackchannelHttpHandler = LoginVerbindung);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Zeit);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Api.DisposeAsync();
        Mitschnitt.Dispose();
        LoginVerbindung.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Verbindung zum Login-Server, deren Token-Erneuerung (grant_type=refresh_token) sich stören lässt.</summary>
public sealed class LoginVerbindung : DelegatingHandler
{
    /// <summary>
    /// <c>null</c> = normal; sonst Antwortstatus der Erneuerung, <c>0</c> = Verbindung bricht ab,
    /// <c>200</c> = HTML-Fehlerseite eines Proxys statt JSON.
    /// </summary>
    public HttpStatusCode? ErneuerungStoeren { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ErneuerungStoeren is { } stoerung && request.Content is not null
            && (await request.Content.ReadAsStringAsync(cancellationToken)).Contains("grant_type=refresh_token", StringComparison.Ordinal))
        {
            return stoerung switch
            {
                0 => throw new HttpRequestException("Login-Server nicht erreichbar."),
                HttpStatusCode.OK => new HttpResponseMessage(stoerung) { Content = new StringContent("<html>Wartung</html>") },
                _ => new HttpResponseMessage(stoerung) { Content = new StringContent("""{"error":"invalid_grant"}""") },
            };
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Merkt sich die Kopfzeilen der letzten Anfrage an die API.</summary>
public sealed class ApiMitschnitt : DelegatingHandler
{
    public System.Net.Http.Headers.HttpRequestHeaders? LetzteKopfzeilen { get; private set; }

    public Uri? LetzteAdresse { get; private set; }

    /// <summary>Alle weitergeleiteten Bearer-Tokens in Reihenfolge.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string?> Tokens { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Tokens.Enqueue(request.Headers.Authorization?.Parameter);
        LetzteKopfzeilen = request.Headers;
        LetzteAdresse = request.RequestUri;
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Systemzeit mit einstellbarem Versatz.</summary>
public sealed class VerstellbareZeit : TimeProvider
{
    public TimeSpan Versatz { get; set; }

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Versatz;
}
