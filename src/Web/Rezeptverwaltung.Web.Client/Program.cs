using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Rezeptverwaltung.Web.Client.Api;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Alle Aufrufe gehen an den eigenen Server (BFF, ADR 0016); der Browser schickt das Sitzungs-Cookie mit.
builder.Services.AddScoped(_ => new HttpClient(new CsrfHeaderHandler { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});
builder.Services.AddScoped<IRezeptApi, RezeptApi>();
builder.Services.AddScoped<Sitzung>();

await builder.Build().RunAsync().ConfigureAwait(false);
