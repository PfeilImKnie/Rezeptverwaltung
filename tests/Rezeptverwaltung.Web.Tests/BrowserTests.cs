using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>
/// Weboberfläche auf echtem Kestrel für einen echten Browser (Chromium über Playwright). Nur so zeigt sich, dass die
/// WebAssembly-App startet, der Login im Browser funktioniert und die Seiten bedienbar sind.
/// </summary>
public sealed class BrowserFactory : WebFactory
{
    public string Adresse { get; private set; } = string.Empty;

    public IPlaywright Playwright { get; private set; } = default!;

    public IBrowser Browser { get; private set; } = default!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        UseKestrel(0);
        StartServer();
        // localhost statt 127.0.0.1: Browser behandeln localhost als sicheren Kontext (Secure-Cookies über http).
        var adresse = new Uri(Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        Adresse = $"http://localhost:{adresse.Port}";

        // Lädt Chromium beim ersten Lauf herunter (CI: vorher mit Systembibliotheken installiert, siehe ci.yml).
        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
        {
            throw new InvalidOperationException("Chromium für Playwright konnte nicht installiert werden.");
        }

        // Der erste Start der WebAssembly-App dauert auf CI-Runnern (mit Coverage) deutlich länger als lokal.
        Assertions.SetDefaultExpectTimeout(30_000);
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync();
        Playwright.Dispose();
        await base.DisposeAsync();
    }
}

public sealed class BrowserTests(BrowserFactory web) : IClassFixture<BrowserFactory>
{
    /// <summary>Optional: Ordner für Bildschirmfotos (z. B. zur Durchsicht der Gestaltung).</summary>
    private static readonly string? Fotoordner = Environment.GetEnvironmentVariable("REZEPTE_SCREENSHOTS");

    private async Task<IPage> AngemeldetAlsAsync(Persona persona, string anzeigename)
    {
        var kontext = await web.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "de-DE",
            ViewportSize = new ViewportSize { Width = 1024, Height = 1366 }, // Tablet hochkant
        });
        var seite = await kontext.NewPageAsync();
        seite.SetDefaultTimeout(30_000);
        // Diagnose für die CI: was der Browser meldet, wenn die App nicht startet.
        var protokoll = new System.Collections.Concurrent.ConcurrentQueue<string>();
        seite.Console += (_, meldung) => protokoll.Enqueue($"console.{meldung.Type}: {meldung.Text}");
        seite.PageError += (_, fehler) => protokoll.Enqueue($"pageerror: {fehler}");
        seite.RequestFailed += (_, anfrage) => protokoll.Enqueue($"requestfailed: {anfrage.Url} {anfrage.Failure}");
        seite.Response += (_, antwort) =>
        {
            if (antwort.Status >= 400)
            {
                protokoll.Enqueue($"http {antwort.Status}: {antwort.Url}");
            }
        };

        await seite.GotoAsync(web.Adresse);
        // Login-Seite des Test-Login-Servers (statt Zitadel).
        await seite.FillAsync("input[name=username]", persona.Name);
        await seite.FillAsync("textarea[name=claims]", TestLogin.LoginClaims(persona, anzeigename));
        await seite.ClickAsync("input[type=submit]");

        for (var versuch = 1; ; versuch++)
        {
            try
            {
                await Assertions.Expect(seite.Locator(".kopf .person")).ToHaveTextAsync(anzeigename);
                return seite;
            }
            catch (PlaywrightException ausnahme)
            {
                // Auf CI-Runnern starten parallel laufende Testprojekte Docker-Container. Die neuen Netzwerkschnittstellen
                // lassen Chromium laufende Downloads abbrechen (ERR_NETWORK_CHANGED). Nur dann neu laden; die Sitzung bleibt.
                if (versuch < 3 && protokoll.Any(m => m.Contains("ERR_NETWORK_CHANGED", StringComparison.Ordinal)))
                {
                    protokoll.Clear();
                    await seite.ReloadAsync();
                    continue;
                }

                throw new InvalidOperationException(
                    $"App nach dem Login nicht bereit. Browser meldete:{Environment.NewLine}{string.Join(Environment.NewLine, protokoll)}", ausnahme);
            }
        }
    }

    private static async Task FotoAsync(IPage seite, string name)
    {
        if (Fotoordner is not null)
        {
            Directory.CreateDirectory(Fotoordner);
            await seite.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(Fotoordner, $"{name}.png"), FullPage = true });
        }
    }

    [Fact]
    public async Task Rezept_anlegen_umrechnen_bearbeiten_und_freigeben()
    {
        var name = $"Sachertorte {Guid.NewGuid():N}"[..20];

        // Konditor/in legt an und rechnet um, darf aber nicht freigeben.
        var konditor = await AngemeldetAlsAsync(Persona.KonditorA, "Konrad Konditor");
        await Assertions.Expect(konditor.Locator("h1")).ToHaveTextAsync("Rezepte");
        await FotoAsync(konditor, "1-liste-leer");
        await konditor.ClickAsync("text=+ Neues Rezept");
        await konditor.FillAsync("label.name input", name);
        await konditor.ClickAsync(".form-editor >> text=Ring");
        await konditor.FillAsync(".form-editor input[placeholder='26']", "26");
        await konditor.FillAsync(".form-editor input[placeholder='6']", "6");
        var zutaten = new (string Name, string Gramm)[] { ("Butter", "140"), ("Zucker", "110"), ("Eier", "300") };
        for (var i = 0; i < zutaten.Length; i++)
        {
            await konditor.Locator("input.bezeichnung").Nth(i).FillAsync(zutaten[i].Name);
            await konditor.Locator(".gramm input").Nth(i).FillAsync(zutaten[i].Gramm);
        }

        await FotoAsync(konditor, "2-neues-rezept");
        await konditor.ClickAsync("button:text('Anlegen')");

        await Assertions.Expect(konditor.Locator("h1")).ToHaveTextAsync(name);
        await Assertions.Expect(konditor.Locator("table.zutaten tfoot")).ToContainTextAsync("550 g");
        await konditor.ClickAsync(".umrechnen .auswahl >> text=Ring");
        await konditor.Locator(".umrechnen form input").First.FillAsync("18");
        await konditor.ClickAsync(".umrechnen button[type=submit]");
        await Assertions.Expect(konditor.Locator(".ergebnis")).ToContainTextAsync("Faktor 0,4793");
        await Assertions.Expect(konditor.Locator(".ergebnis tbody tr").First).ToContainTextAsync("67,1 g");
        await FotoAsync(konditor, "3-rezept-umgerechnet");

        await konditor.ClickAsync("text=Entwurf (Fassung 1) bearbeiten");
        await Assertions.Expect(konditor.Locator("button:text('Freigeben')")).ToHaveCountAsync(0);
        await konditor.Locator(".gramm input").First.FillAsync("150");
        await konditor.ClickAsync("button:text('Speichern')");
        await Assertions.Expect(konditor.Locator(".hinweis")).ToHaveTextAsync("Freigeben kann die Inhaberin oder der Inhaber.");
        await FotoAsync(konditor, "4-entwurf");

        // Inhaber/in gibt frei.
        var inhaber = await AngemeldetAlsAsync(Persona.InhaberA, "Ina Inhaberin");
        await inhaber.FillAsync("input.suche", name);
        await inhaber.ClickAsync($".rezeptliste >> text={name}");
        await inhaber.ClickAsync("text=Entwurf (Fassung 1) bearbeiten");
        await Assertions.Expect(inhaber.Locator(".gramm input").First).ToHaveValueAsync("150");
        await inhaber.ClickAsync("button:text('Freigeben')");
        await Assertions.Expect(inhaber.Locator(".fassung-info")).ToContainTextAsync("Fassung 1, freigegeben");
        await Assertions.Expect(inhaber.Locator("button:text('Neuen Entwurf anlegen')")).ToBeVisibleAsync();
        await FotoAsync(inhaber, "5-freigegeben");
    }

    [Fact]
    public async Task Mitarbeiter_liest_und_rechnet_um_ohne_Bearbeiten()
    {
        var mitarbeiter = await AngemeldetAlsAsync(Persona.MitarbeiterA, "Mia Mitarbeiterin");

        await Assertions.Expect(mitarbeiter.Locator("h1")).ToHaveTextAsync("Rezepte");
        await Assertions.Expect(mitarbeiter.Locator("text=+ Neues Rezept")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Abmelden_fuehrt_zurueck_zum_Login()
    {
        var seite = await AngemeldetAlsAsync(Persona.InhaberA, "Ina Inhaberin");

        await seite.ClickAsync("button:text('Abmelden')");

        await Assertions.Expect(seite.Locator("input[name=username]")).ToBeVisibleAsync();
        var benutzer = await seite.APIRequest.GetAsync($"{web.Adresse}/bff/benutzer", new APIRequestContextOptions
        {
            Headers = new Dictionary<string, string> { ["X-CSRF"] = "1" },
        });
        benutzer.Status.ShouldBe(401);
        await seite.GotoAsync(web.Adresse);
        await Assertions.Expect(seite.Locator("input[name=username]")).ToBeVisibleAsync(); // wieder zum Login
    }
}
