using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Testperson: Name = Persona im Login-Server; Rollen je Betrieb wie im Zitadel-Token.</summary>
public sealed record Persona(string Name, params (string Rolle, string Betrieb)[] Rollen)
{
    /// <summary>Zielgruppe im Token; nur für den Test „falsche Zielgruppe“ abweichend.</summary>
    public string Zielgruppe { get; init; } = TestLogin.Zielgruppe;

    /// <summary>Weitere Claims, die die Vorgaben des Login-Servers überschreiben (z. B. <c>exp</c>, <c>nbf</c>).</summary>
    public IReadOnlyDictionary<string, object> Zusatzclaims { get; init; } = new Dictionary<string, object>();

    public static readonly Persona InhaberA = new("inhaber-a", ("inhaber", "betrieb-a"));
    public static readonly Persona KonditorA = new("konditor-a", ("konditor", "betrieb-a"));
    public static readonly Persona MitarbeiterA = new("mitarbeiter-a", ("mitarbeiter", "betrieb-a"));
    public static readonly Persona InhaberB = new("inhaber-b", ("inhaber", "betrieb-b"));
    public static readonly Persona OhneRolle = new("ohne-rolle");
    public static readonly Persona ZweiBetriebe = new("zwei-betriebe", ("inhaber", "betrieb-a"), ("mitarbeiter", "betrieb-b"));
    public static readonly Persona UnbekannteRolle = new("unbekannte-rolle", ("chef", "betrieb-a"));
    public static readonly Persona FremdeZielgruppe = new("fremde-zielgruppe", ("inhaber", "betrieb-a")) { Zielgruppe = "andere-api" };


    /// <summary>Token, der seit einer Stunde abgelaufen ist (weit außerhalb der erlaubten Uhrabweichung).</summary>
    public static readonly Persona Abgelaufen = new("abgelaufen", ("inhaber", "betrieb-a"))
    {
        Zusatzclaims = new Dictionary<string, object>
        {
            ["iat"] = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds(),
            ["nbf"] = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds(),
            ["exp"] = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(),
        },
    };

    /// <summary>Token, der erst in einer Stunde gültig wird.</summary>
    public static readonly Persona NochNichtGueltig = new("noch-nicht-gueltig", ("inhaber", "betrieb-a"))
    {
        Zusatzclaims = new Dictionary<string, object>
        {
            ["nbf"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            ["exp"] = DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds(),
        },
    };

    /// <summary>Rollenname nicht exakt wie vereinbart („Inhaber“ statt „inhaber“): kein Recht.</summary>
    public static readonly Persona RolleGrossgeschrieben = new("rolle-grossgeschrieben", ("Inhaber", "betrieb-a"));

    /// <summary>Mitarbeiter/in in Betrieb A, dazu eine unbekannte Rolle in Betrieb B. Unbekannte Rollen zählen nicht.</summary>
    public static readonly Persona MitarbeiterAMitFremderUnbekannterRolle = new("mitarbeiter-a-chef-b", ("mitarbeiter", "betrieb-a"), ("chef", "betrieb-b"));

    public static readonly Persona[] Alle =
    [
        InhaberA, KonditorA, MitarbeiterA, InhaberB, OhneRolle, ZweiBetriebe, UnbekannteRolle, FremdeZielgruppe,
        Abgelaufen, NochNichtGueltig, RolleGrossgeschrieben, MitarbeiterAMitFremderUnbekannterRolle,
    ];
}

/// <summary>
/// Echter OIDC-Aussteller für Tests (mock-oauth2-server, ADR 0015): Die API prüft Tokens genauso wie in Produktion
/// (Signatur über JWKS, Aussteller, Zielgruppe, Gültigkeit). Ein Container für alle Testklassen.
/// </summary>
public static class TestLogin
{
    public const string Image = "ghcr.io/navikt/mock-oauth2-server:6.1.4";
    public const string Aussteller = "rezeptverwaltung";
    public const string Zielgruppe = "rezeptverwaltung-api";

    private static readonly SemaphoreSlim Sperre = new(1, 1);
    private static readonly Dictionary<string, string> Tokens = [];
    private static readonly HttpClient Http = new();
    private static IContainer? _container;

    public static string Authority =>
        $"http://{_container!.Hostname}:{_container.GetMappedPublicPort(8080)}/{Aussteller}";

    /// <summary>Konfiguration der API für diesen Aussteller.</summary>
    public static IReadOnlyDictionary<string, string?> Einstellungen => new Dictionary<string, string?>
    {
        ["Authentifizierung:Authority"] = Authority,
        ["Authentifizierung:Audience"] = Zielgruppe,
        ["Authentifizierung:RequireHttpsMetadata"] = "false",
    };

    public static async Task StartenAsync()
    {
        await Sperre.WaitAsync();
        try
        {
            if (_container is not null)
            {
                return;
            }

            var container = new ContainerBuilder(Image)
                .WithPortBinding(8080, assignRandomHostPort: true)
                .WithEnvironment("JSON_CONFIG", Konfiguration())
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
                    .ForPort(8080)
                    .ForPath($"/{Aussteller}/.well-known/openid-configuration")))
                .Build();
            await container.StartAsync();
            _container = container;
        }
        finally
        {
            Sperre.Release();
        }
    }

    public static async Task<string> TokenAsync(Persona persona)
    {
        await StartenAsync();
        lock (Tokens)
        {
            if (Tokens.TryGetValue(persona.Name, out var vorhanden))
            {
                return vorhanden;
            }
        }

        using var antwort = await Http.PostAsync(
            $"{Authority}/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "tests",
                ["client_secret"] = "geheim",
                ["scope"] = persona.Name,
            }));
        antwort.EnsureSuccessStatusCode();
        var token = (await antwort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
        lock (Tokens)
        {
            Tokens[persona.Name] = token;
        }

        return token;
    }

    public static AuthenticationHeaderValue Header(string token) => new("Bearer", token);

    /// <summary>Token eines fremden Ausstellers (anderer Pfad im Login-Server, also anderes „iss“).</summary>
    public static async Task<string> FremderAusstellerTokenAsync()
    {
        await StartenAsync();
        var fremd = Authority.Replace($"/{Aussteller}", "/fremd", StringComparison.Ordinal);
        using var antwort = await Http.PostAsync(
            $"{fremd}/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "tests",
                ["client_secret"] = "geheim",
                ["scope"] = Zielgruppe,
            }));
        antwort.EnsureSuccessStatusCode();
        return (await antwort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }

    /// <summary>JSON_CONFIG des Login-Servers: je Persona ein Token mit Rollen im Zitadel-Format.</summary>
    private static string Konfiguration()
    {
        var zuordnungen = Persona.Alle.Select(persona =>
        {
            var claims = new Dictionary<string, object>
            {
                ["sub"] = persona.Name,
                ["aud"] = new[] { persona.Zielgruppe },
            };
            if (persona.Rollen.Length > 0)
            {
                claims["urn:zitadel:iam:org:project:roles"] = persona.Rollen
                    .GroupBy(r => r.Rolle)
                    .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Betrieb, r => $"{r.Betrieb}.test"));
            }

            foreach (var (name, wert) in persona.Zusatzclaims)
            {
                claims[name] = wert;
            }

            return new { requestParam = "scope", match = persona.Name, claims };
        });

        return JsonSerializer.Serialize(new
        {
            interactiveLogin = false,
            tokenCallbacks = new[] { new { issuerId = Aussteller, tokenExpiry = 3600, requestMappings = zuordnungen } },
        });
    }
}
