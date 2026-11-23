# Rezeptverwaltung

Rezeptverwaltung für eine Konditormeisterin. API-first, DDD, .NET 10. Geplante Frontends: Tablet-PWA, Alexa-Skill.

## Befehle

```bash
dotnet build Rezeptverwaltung.slnx                        # Warnungen = Fehler
dotnet test --solution Rezeptverwaltung.slnx              # Microsoft Testing Platform (global.json)
dotnet format Rezeptverwaltung.slnx                       # vor jedem Commit
cd tests/Rezeptverwaltung.Rezeptur.Domain.Tests && dotnet tool restore && dotnet stryker   # Mutation Testing, Schwelle 80 %
dotnet run --project src/Host/Rezeptverwaltung.AppHost    # lokal: Aspire startet PostgreSQL (Podman/Docker) + API auf :5080
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
tests/                            Domain-, Application-, API-, Architektur- und Persistenztests (Testcontainers)
```

- Module greifen nicht aufeinander zu. Neue Module werden in `tests/Rezeptverwaltung.ArchitectureTests/ArchitekturTests.cs` → `Module` eingetragen.
- Fachregeln: `DomainRuleViolationException.Wenn(...)` = ungültige Eingabe → 422, `.ZustandWenn(...)` = Zustand lässt es nicht zu → 409, jeweils mit `code`. Eingaben vor dem Zustand prüfen. Codes sind stabil (`bereich.regel`, snake_case) (ADR 0009).
- Zahlen strikt: keine Zahlen als JSON-Strings; Gramm in Query-Parametern über den Typ `Gramm` binden, nie direkt als `decimal`, weil sonst "1,5" als 15 gelesen wird.
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
