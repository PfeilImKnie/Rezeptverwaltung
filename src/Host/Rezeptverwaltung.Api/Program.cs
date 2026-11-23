using System.Text.Json.Serialization;
using Rezeptverwaltung.Api;
using Rezeptverwaltung.Rezeptur.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Fehlende oder null-Pflichtfelder führen zu 400 statt zu halb initialisierten Objekten.
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
    // Zahlen nur als JSON-Zahlen, nicht als Strings ("100"): der Vertrag sagt type: number.
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi("v1");
builder.Services.AddHealthChecks();

// Verbindung kommt lokal von Aspire (AppHost), im Betrieb aus der Umgebung (ConnectionStrings__rezeptverwaltung).
var verbindung = builder.Configuration.GetConnectionString("rezeptverwaltung");
if (string.IsNullOrWhiteSpace(verbindung))
{
    throw new InvalidOperationException(
        "Verbindungszeichenfolge 'ConnectionStrings:rezeptverwaltung' fehlt oder ist leer. Lokal über den AppHost starten: "
        + "dotnet run --project src/Host/Rezeptverwaltung.AppHost");
}

builder.Services.AddRezepturModule(verbindung);

var app = builder.Build();

// Einzelinstanz-Betrieb: Migrationen beim Start (ADR 0011). Abschaltbar, z. B. für getrennte Migrationsläufe.
if (app.Configuration.GetValue("Persistenz:MigrationenBeimStart", defaultValue: true))
{
    await app.Services.MigriereRezepturDatenbankAsync().ConfigureAwait(false);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapRezepturModule();

await app.RunAsync().ConfigureAwait(false);

#pragma warning disable CA1515 // Program muss für WebApplicationFactory in Tests öffentlich sein.
public partial class Program;
#pragma warning restore CA1515
