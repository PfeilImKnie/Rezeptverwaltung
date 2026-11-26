using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>Weboberfläche außerhalb der Entwicklung (wie hinter dem TLS-Proxy im Betrieb).</summary>
public sealed class BetriebsFactory : WebFactory
{
    // Nicht „Production“: Dort verbietet die Anmeldung den http-Login-Server der Tests (ADR 0015/0016).
    protected override string Umgebung => "Staging";
}

public sealed class BetriebsumgebungTests(BetriebsFactory web) : IClassFixture<BetriebsFactory>
{
    [Fact]
    public void Sitzungs_und_Antiforgery_Cookie_sind_immer_Secure()
    {
        var sitzung = web.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var antiforgery = web.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        sitzung.Cookie.SecurePolicy.ShouldBe(CookieSecurePolicy.Always);
        antiforgery.Cookie.SecurePolicy.ShouldBe(CookieSecurePolicy.Always);
    }

    [Fact]
    public async Task Auch_ohne_HTTPS_beim_Server_sind_die_Login_Cookies_Secure()
    {
        // Hinter Caddy kommt die Anfrage als http an. Die Cookies des Login-Ablaufs (Korrelation, Nonce) müssen trotzdem
        // Secure sein; Sitzungs- und Antiforgery-Cookie prüft der Test oben über ihre Einstellungen.
        using var browser = web.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        using var antwort = await browser.GetAsync("/anmelden", Xunit.TestContext.Current.CancellationToken);

        antwort.Headers.GetValues("Set-Cookie").ShouldAllBe(c => c.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }
}
