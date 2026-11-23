using System.Net;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Jede Operation verlangt einen gültigen Token (ADR 0015). Abgeleitet aus dem Vertrag, damit kein neuer Endpunkt offen bleibt.</summary>
public sealed class AnmeldungApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] HttpMethoden = ["get", "put", "post", "delete", "patch"];
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public static TheoryData<string, string> AlleOperationen()
    {
        var yaml = new YamlStream();
        using (var reader = File.OpenText(Path.Combine(AppContext.BaseDirectory, "openapi.yaml")))
        {
            yaml.Load(reader);
        }

        var daten = new TheoryData<string, string>();
        foreach (var (pfad, methoden) in (YamlMappingNode)((YamlMappingNode)yaml.Documents[0].RootNode)["paths"])
        {
            foreach (var (methode, _) in (YamlMappingNode)methoden)
            {
                var name = ((YamlScalarNode)methode).Value!;
                if (HttpMethoden.Contains(name))
                {
                    daten.Add(name.ToUpperInvariant(), ((YamlScalarNode)pfad).Value!);
                }
            }
        }

        return daten;
    }

    private static HttpRequestMessage Anfrage(string methode, string pfad)
    {
        var url = pfad.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal).Replace("{nummer}", "1", StringComparison.Ordinal)
            + "?zielgewichtGramm=100&durchmesserCm=20&laengeCm=20&breiteCm=20&hoeheCm=5";
        var anfrage = new HttpRequestMessage(new HttpMethod(methode), url);
        if (methode is "POST" or "PUT")
        {
            anfrage.Content = new StringContent("""{"name":"x","zutaten":[]}""", Encoding.UTF8, "application/json");
        }

        anfrage.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        return anfrage;
    }

    [Theory]
    [MemberData(nameof(AlleOperationen))]
    public async Task Ohne_Token_liefert_jede_Operation_401(string methode, string pfad)
    {
        using var client = factory.CreateClientOhneAnmeldung();

        var antwort = await client.SendAsync(Anfrage(methode, pfad), _ct);

        antwort.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{methode} {pfad}");
    }

    [Theory]
    [MemberData(nameof(AlleOperationen))]
    public async Task Gefaelschter_Token_liefert_jede_Operation_401(string methode, string pfad)
    {
        using var client = factory.CreateClientOhneAnmeldung();
        var echt = await TestLogin.TokenAsync(Persona.InhaberA);
        var teile = echt.Split('.');
        var gefaelscht = $"{teile[0]}.{teile[1]}.{new string('A', teile[2].Length)}"; // Signatur zerstört
        using var anfrage = Anfrage(methode, pfad);
        anfrage.Headers.Authorization = TestLogin.Header(gefaelscht);

        (await client.SendAsync(anfrage, _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unsignierter_Token_wird_abgewiesen()
    {
        using var client = factory.CreateClientOhneAnmeldung();
        var teile = (await TestLogin.TokenAsync(Persona.InhaberA)).Split('.');
        var kopf = Convert.ToBase64String("""{"alg":"none","typ":"JWT"}"""u8.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        client.DefaultRequestHeaders.Authorization = TestLogin.Header($"{kopf}.{teile[1]}.");

        (await client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_mit_fremder_Zielgruppe_wird_abgewiesen()
    {
        using var client = await factory.CreateClientAlsAsync(Persona.FremdeZielgruppe);

        (await client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_eines_fremden_Ausstellers_wird_abgewiesen()
    {
        using var client = factory.CreateClientOhneAnmeldung();
        client.DefaultRequestHeaders.Authorization = TestLogin.Header(await TestLogin.FremderAusstellerTokenAsync());

        (await client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthorized_liefert_Problem_Details()
    {
        using var client = factory.CreateClientOhneAnmeldung();

        var antwort = await client.GetAsync($"/api/v1/rezepte/{Guid.NewGuid()}", _ct);

        antwort.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        antwort.Headers.WwwAuthenticate.ShouldContain(h => h.Scheme == "Bearer");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/openapi/v1.json")]
    public async Task Gesundheit_und_Vertrag_sind_ohne_Anmeldung_erreichbar(string pfad)
    {
        using var client = factory.CreateClientOhneAnmeldung();

        (await client.GetAsync(pfad, _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
