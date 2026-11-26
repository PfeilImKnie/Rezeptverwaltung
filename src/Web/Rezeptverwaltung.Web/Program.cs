using Microsoft.AspNetCore.DataProtection;
using Rezeptverwaltung.Web;
using Rezeptverwaltung.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Die Weboberfläche nimmt nur kleine Anfragen an; die API begrenzt zusätzlich selbst (ADR 0015).
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = ApiWeiterleitung.MaximaleAnfrageBytes);

builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
builder.Services.AddWebAnmeldung(builder.Configuration, builder.Environment);
builder.Services.AddApiWeiterleitung(builder.Configuration);
builder.Services.AddHealthChecks();

// Schlüssel für Sitzungs-Cookie und Antiforgery. Im Betrieb in einem Volume, damit ein Neustart nicht alle abmeldet.
var schluessel = builder.Configuration["DataProtection:Verzeichnis"];
var datenschutz = builder.Services.AddDataProtection().SetApplicationName("Rezeptverwaltung.Web");
if (!string.IsNullOrWhiteSpace(schluessel))
{
    datenschutz.PersistKeysToFileSystem(new DirectoryInfo(schluessel));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseCsrfSchutz();

// Bewusst ohne Anmeldung: statische Dateien (App-Code ist ohnehin öffentlich) und Gesundheitsprüfung.
app.MapStaticAssets().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapAnmeldung();
app.MapApiWeiterleitung();

// Jede Seite verlangt Anmeldung (Fallback-Policy): Ohne Sitzung leitet der Server direkt zum Login weiter.
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Rezeptverwaltung.Web.Client.Routes).Assembly);

await app.RunAsync().ConfigureAwait(false);

#pragma warning disable CA1515 // Program muss für WebApplicationFactory in Tests öffentlich sein.
public partial class Program;
#pragma warning restore CA1515
