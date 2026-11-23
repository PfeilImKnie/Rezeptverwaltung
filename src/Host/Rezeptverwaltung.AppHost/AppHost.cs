// Lokale Entwicklungsumgebung (ADR 0012): startet PostgreSQL im Container (Docker oder Podman) und die API.
// Start: dotnet run --project src/Host/Rezeptverwaltung.AppHost
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine") // gleiche Hauptversion wie in Tests und Betrieb
    .WithDataVolume("rezeptverwaltung-postgres-daten") // Rezepte überleben einen Neustart
    .WithLifetime(ContainerLifetime.Persistent);

var datenbank = postgres.AddDatabase("rezeptverwaltung");

builder.AddDotnetProject("api", "../Rezeptverwaltung.Api/Rezeptverwaltung.Api.csproj")
    .WithReference(datenbank)
    .WaitFor(datenbank);

await builder.Build().RunAsync().ConfigureAwait(false);
