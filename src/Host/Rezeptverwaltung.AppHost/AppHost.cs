using System.Text.Json;

// Lokale Entwicklungsumgebung (ADR 0012): startet PostgreSQL und einen Test-Login (OIDC) im Container
// (Docker oder Podman) sowie die API.
// Start: dotnet run --project src/Host/Rezeptverwaltung.AppHost
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine") // gleiche Hauptversion wie in Tests und Betrieb
    .WithDataVolume("rezeptverwaltung-postgres-daten") // Rezepte überleben einen Neustart
    .WithLifetime(ContainerLifetime.Persistent);

var datenbank = postgres.AddDatabase("rezeptverwaltung");

// Test-Login (ADR 0015): stellt Tokens in derselben Form wie Zitadel aus. Nur für die Entwicklung;
// im Betrieb ist der Identity Provider Zitadel (ADR 0004/0005).
const string Aussteller = "rezeptverwaltung";
const string Zielgruppe = "rezeptverwaltung-api";
var personen = new (string Scope, string Rolle)[] { ("inhaber", "inhaber"), ("konditor", "konditor"), ("mitarbeiter", "mitarbeiter") };
var loginKonfiguration = JsonSerializer.Serialize(new
{
    interactiveLogin = false,
    tokenCallbacks = new[]
    {
        new
        {
            issuerId = Aussteller,
            tokenExpiry = 8 * 3600,
            requestMappings = personen.Select(p => new
            {
                requestParam = "scope",
                match = p.Scope,
                claims = new Dictionary<string, object>
                {
                    ["sub"] = $"entwicklung-{p.Rolle}",
                    ["aud"] = new[] { Zielgruppe },
                    ["urn:zitadel:iam:org:project:roles"] = new Dictionary<string, object>
                    {
                        [p.Rolle] = new Dictionary<string, string> { ["entwicklung"] = "entwicklung.local" },
                    },
                },
            }).ToArray(),
        },
    },
});

var login = builder.AddContainer("login", "ghcr.io/navikt/mock-oauth2-server", "6.1.4")
    .WithHttpEndpoint(port: 18080, targetPort: 8080, name: "http")
    .WithEnvironment("JSON_CONFIG", loginKonfiguration);

builder.AddDotnetProject("api", "../Rezeptverwaltung.Api/Rezeptverwaltung.Api.csproj")
    .WithReference(datenbank)
    .WaitFor(datenbank)
    .WaitFor(login)
    .WithEnvironment("Authentifizierung__Authority", $"http://localhost:18080/{Aussteller}")
    .WithEnvironment("Authentifizierung__Audience", Zielgruppe)
    .WithEnvironment("Authentifizierung__RequireHttpsMetadata", "false");

await builder.Build().RunAsync().ConfigureAwait(false);
