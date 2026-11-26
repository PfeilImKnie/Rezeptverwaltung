# Rezeptverwaltung

Rezeptverwaltung für eine Konditormeisterin. API-first, DDD, .NET 10. Frontends: Weboberfläche (Blazor, Tablet-PWA), geplant Alexa-Skill.

## Befehle

```bash
dotnet build Rezeptverwaltung.slnx                        # Warnungen = Fehler
dotnet test --solution Rezeptverwaltung.slnx              # Microsoft Testing Platform (global.json)
dotnet format Rezeptverwaltung.slnx                       # vor jedem Commit
cd tests/Rezeptverwaltung.Rezeptur.Domain.Tests && dotnet tool restore && dotnet stryker   # Mutation Testing, Schwelle 80 %
dotnet run --project src/Host/Rezeptverwaltung.AppHost    # lokal: Aspire startet PostgreSQL + Test-Login (:18080) + API (:5080) + Weboberfläche (:5090)
# Neue Migration nach Modelländerung (Test „Modell_und_Migrationen_stimmen_ueberein“ erzwingt das):
dotnet ef migrations add <Name> --project src/Modules/Rezeptur/Rezeptverwaltung.Rezeptur.Infrastructure \
  --startup-project src/Modules/Rezeptur/Rezeptverwaltung.Rezeptur.Infrastructure --output-dir Persistence/Migrations
```

## Arbeitsweise (verbindlich)

- **Jeder PR entsteht über `/pr-erstellen`.** Das schließt unabhängigen Test (`/pr-test`), Dokumentation (`/adr`) und unabhängiges Code-Review (`/pr-review`) ein (ADR 0007). Nie direkt auf `main` committen.
- Ausnahme: reine Dependabot-PRs sind vom PR-Gate befreit (ADR 0010). Ein menschlicher Commit auf den Branch beendet sie dauerhaft; dann gilt `/pr-erstellen`. Eine geänderte Beschreibung löst das Gate nur für diesen Lauf aus.
- **Definition of Done:** [docs/definition-of-done.md](docs/definition-of-done.md)
- **API-Änderungen beginnen in `api/openapi.yaml`**, danach Implementierung (ADR 0003). Einschränkungen in Vertrag und Domain müssen identisch sein.
- Architekturentscheidungen: [docs/adr/](docs/adr/README.md). Nicht stillschweigend von einer ADR abweichen, sondern eine neue ADR schreiben, die sie ablöst.

## Architektur (ADR 0002)

```
src/SharedKernel/                 DDD-Bausteine, keine Fachlichkeit
src/Modules/<Kontext>/
  *.Domain                        Aggregate, Value Objects. KEINE Framework-Abhängigkeiten
  *.Application                   Use-Case-Handler + Ports (Interfaces)
  *.Infrastructure                Adapter: Http/ (Endpoints, API-Verträge), Persistence/
                                  öffentlich nur <Kontext>Module (AddXModule / MapXModule)
src/Host/Rezeptverwaltung.Api     Komposition, Problem Details, OpenAPI, Migration beim Start
src/Host/Rezeptverwaltung.AppHost Aspire (nur lokale Entwicklung)
src/Web/Rezeptverwaltung.Web      Weboberfläche: Server (BFF: Login, Cookie, Weiterleitung /api → API mit Token)
src/Web/Rezeptverwaltung.Web.Client  Seiten (Blazor WebAssembly), spricht nur über /api und /bff mit dem eigenen Server
tests/                            Domain-, Application-, API-, Architektur-, Persistenz- (Testcontainers) und Web-Tests;
                                  Testumgebung = gemeinsamer Login-Server + API-Fabrik
```

- Module greifen nicht aufeinander zu. Neue Module werden in `tests/Rezeptverwaltung.ArchitectureTests/ArchitekturTests.cs` → `Module` eingetragen.
- Fachregeln: `DomainRuleViolationException.Wenn(...)` = ungültige Eingabe → 422, `.ZustandWenn(...)` = Zustand lässt es nicht zu → 409, jeweils mit `code`. Eingaben vor dem Zustand prüfen. Codes sind stabil (`bereich.regel`, snake_case) (ADR 0009).
- Anmeldung, Betriebe, Rollen (ADR 0015): Jeder Endpunkt verlangt Anmeldung (Fallback-Policy, Ausnahmen nur `/health`, `/openapi`). Jeder Use Case prüft zuerst das Recht (`ZugriffVerweigertException.Pruefen` mit `Rechte.*`) und behandelt Rezepte anderer Betriebe als nicht vorhanden (`Rechte.NurEigenes`). Jedes neue Aggregat bekommt eine `BetriebId` und einen Mandantenfilter (`HasQueryFilter`). Prüfreihenfolge: 413 → 401 → 403 → 404 → 428/412 → 422 → 409.
- Web-Tests: Seiten mit bUnit gegen `FakeRezeptApi`; Server mit `WebFactory` (echter Login + echte API, `AngemeldetAlsAsync`); Kernabläufe mit Playwright in `BrowserTests` (Bildschirmfotos mit `REZEPTE_SCREENSHOTS=<ordner>`). API und Web haben beide `Program`: Web-Projekt mit `Aliases="WebHost"` referenzieren.
- Tests: API-Clients sind als Inhaber/in von Betrieb A angemeldet (`ApiFactory`, echte Tokens vom Test-Login); andere Personen über `CreateClientAlsAsync(Persona.…)`. Neue Operationen werden über `AnmeldungApiTests` automatisch auf 401 geprüft.
- Weboberfläche (ADR 0016): kennt nur den HTTP-Vertrag (ArchUnit), nie Module oder SharedKernel. Nie einen Token in den Browser geben; Browser-Aufrufe gehen an `/api/**` (Weiterleitung mit Bearer) oder `/bff/**` und tragen `X-CSRF: 1`. Rechte nur ausblenden (`/rezepte/berechtigungen`), prüfen tut die API. Zahlen ein- und ausgeben nur über `Zahlen` (Komma, keine Tausenderpunkte bei der Eingabe). Editorfelder mit `@bind:event="oninput"`, damit „Speichern“ sofort aktiv wird.
- Fassungen (ADR 0013): Änderungen nur am Entwurf, freigegebene Fassungen sind unveränderlich, Nummern nie wiederverwenden. Ändernde Endpunkte verlangen `If-Match` (Stand = ETag aus `IRezeptRepository.StandVon`), Prüfreihenfolge 404 → 428/412 → 422 → 409. Zeitpunkte aus `TimeProvider`, nie `DateTime.Now`.
- Bezugsform (ADR 0014): optional pro Fassung, Ring oder Blech, cm auf 0,1 genau; Umrechnen über das Volumen. `PUT …/entwurf` ersetzt den Inhalt vollständig (ohne `bezugsform` = ohne Form). Faktoren in der Ausgabe auf 12 signifikante Stellen runden (nie auf Nachkommastellen, sonst werden kleine Faktoren 0).
- Zahlen strikt: keine Zahlen als JSON-Strings; Gramm/Zentimeter in Query-Parametern über die Typen `Gramm`/`Zentimeter` binden, nie direkt als `decimal`, weil sonst "1,5" als 15 gelesen wird.
- Texte strikt: Länge und Leerraum über `Freitext` (Codepoints, ausdrücklich aufgezählter Leerraum), nie `string.Length` oder `Trim()` direkt. Im Vertrag nie `\s` verwenden, weil es je nach Regex-Engine anders ausgelegt wird; stattdessen dieselbe Menge wie `Freitext.IstLeerraum` aufzählen (ADR 0009).
- Value Objects als `readonly record struct` oder `sealed record` mit Factory (`Von`, `Erfasst`) und Validierung. Aggregate sind `sealed`.
- Handler sind einfache Klassen, kein MediatR.
- Persistenz (ADR 0011): ein Schema pro Modul, ein Datensatz pro Aggregat, Listen im Aggregat als JSONB (Reihenfolge!). Mapping nur in `Infrastructure/Persistence`, Domain bleibt EF-frei. Value Objects über `HasConversion` mit den Domain-Factories.
- Tests gegen die Datenbank immer mit echtem PostgreSQL (Testcontainers), nie mit EF-InMemory oder SQLite.

## Konventionen

- **Sprache:** Fachbegriffe deutsch (siehe [Glossar](docs/glossar.md)), technische Begriffe englisch (`Repository`, `Handler`, `Async`). Umlaute in Bezeichnern umschreiben (`ae`, `oe`, `ue`, `ss`).
- **Gewichte:** `decimal` in Gramm. Eingaben auf 0,01 g genau (`Gewicht.Erfasst`). Intern ungerundet rechnen, erst bei der Ausgabe runden.
- **Tests:** xUnit v3, Shouldly (nicht FluentAssertions), CsCheck für Eigenschaften. Testnamen als deutsche Sätze mit Unterstrichen. `TestContext.Current.CancellationToken` übergeben. Fachregeln mit `.ShouldHaveCode("…")` prüfen.
- Pakete zentral in `Directory.Packages.props` (Central Package Management).
