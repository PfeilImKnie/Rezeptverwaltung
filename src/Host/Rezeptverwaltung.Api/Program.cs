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

builder.Services.AddRezepturModule();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapRezepturModule();

await app.RunAsync().ConfigureAwait(false);

#pragma warning disable CA1515 // Program muss für WebApplicationFactory in Tests öffentlich sein.
public partial class Program;
#pragma warning restore CA1515
