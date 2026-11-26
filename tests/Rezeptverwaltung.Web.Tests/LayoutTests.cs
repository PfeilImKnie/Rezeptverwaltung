using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Rezeptverwaltung.Web.Client.Api;
using Rezeptverwaltung.Web.Client.Layout;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>Rahmen der App: angemeldete Person, Abmelden, Hinweis ohne Rolle.</summary>
public sealed class LayoutTests : SeitenTestBasis
{
    private readonly BenutzerServer _server = new();

    public LayoutTests()
    {
        Services.AddSingleton(new HttpClient(_server) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<Sitzung>();
    }

    private IRenderedComponent<MainLayout> Layout() =>
        Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p id='inhalt'>Inhalt</p>"))));

    [Fact]
    public void Zeigt_Name_Inhalt_und_Abmelden_mit_Antiforgery_Token()
    {
        var layout = Layout();

        layout.WaitForAssertion(() => layout.Find(".person").TextContent.ShouldBe("Anna Inhaberin"));
        layout.Find("#inhalt").TextContent.ShouldBe("Inhalt");
        var abmelden = layout.Find("form.abmelden");
        abmelden.GetAttribute("method").ShouldBe("post");
        abmelden.GetAttribute("action").ShouldBe("abmelden");
        abmelden.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value").ShouldBe("token-123");
    }

    [Fact]
    public void Ohne_Rolle_im_Betrieb_gibt_es_einen_Hinweis_statt_Inhalt()
    {
        Api.Rechte = Berechtigungen.Keine;

        var layout = Layout();

        layout.WaitForAssertion(() => layout.Find("[role=alert]").TextContent.ShouldContain("noch keine Rolle in einem Betrieb"));
        layout.FindAll("#inhalt").ShouldBeEmpty();
    }

    [Fact]
    public void Rollen_in_mehreren_Betrieben_werden_erklaert()
    {
        Api.NaechsterFehler = new ApiFehlerException(403, "betrieb.nicht_eindeutig", "Die Anmeldung gilt für mehrere Betriebe.");

        var layout = Layout();

        layout.WaitForAssertion(() => layout.Find("[role=alert]").TextContent.ShouldContain("mehrere Betriebe"));
        layout.FindAll("#inhalt").ShouldBeEmpty();
        layout.Find(".person").TextContent.ShouldBe("Anna Inhaberin"); // Abmelden bleibt möglich
    }

    [Fact]
    public void Server_nicht_erreichbar()
    {
        _server.Erreichbar = false;

        var layout = Layout();

        layout.WaitForAssertion(() => layout.Find("[role=alert]").TextContent.ShouldContain("Keine Verbindung zum Server"));
    }

    [Fact]
    public void Abgelaufene_Sitzung_fuehrt_zum_Login_statt_zu_einer_falschen_Meldung()
    {
        _server.Status = HttpStatusCode.Unauthorized;
        Navigation.NavigateTo("rezepte/neu");

        var layout = Layout();

        layout.WaitForAssertion(() => Navigation.Uri.ShouldBe($"http://localhost/anmelden?returnUrl={Uri.EscapeDataString("/rezepte/neu")}"));
        layout.Markup.ShouldNotContain("Keine Verbindung");
    }

    protected override void Dispose(bool disposing)
    {
        _server.Dispose();
        base.Dispose(disposing);
    }

    private sealed class BenutzerServer : HttpMessageHandler
    {
        public bool Erreichbar { get; set; } = true;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!Erreichbar)
            {
                throw new HttpRequestException("keine Verbindung");
            }

            request.RequestUri!.AbsolutePath.ShouldBe("/bff/benutzer");
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent("""{"name":"Anna Inhaberin","abmeldeToken":"token-123"}""", Encoding.UTF8, "application/json"),
            });
        }
    }
}
