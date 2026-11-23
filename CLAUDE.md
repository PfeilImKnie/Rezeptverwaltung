# Rezeptverwaltung

Rezeptverwaltung für eine Konditormeisterin. API-first, DDD, .NET 10. Geplante Frontends: Tablet-PWA, Alexa-Skill.

## Befehle

```bash
dotnet build Rezeptverwaltung.slnx                        # Warnungen = Fehler
dotnet test --solution Rezeptverwaltung.slnx              # Microsoft Testing Platform (global.json)
dotnet format Rezeptverwaltung.slnx                       # vor jedem Commit
cd tests/Rezeptverwaltung.Rezeptur.Domain.Tests && dotnet tool restore && dotnet stryker   # Mutation Testing, Schwelle 80 %
dotnet run --project src/Host/Rezeptverwaltung.Api        # API, OpenAPI unter /openapi/v1.json
```

## Arbeitsweise (verbindlich)

- **Jeder PR entsteht über `/pr-erstellen`.** Das schließt unabhängigen Test (`/pr-test`), Dokumentation (`/adr`) und unabhängiges Code-Review (`/pr-review`) ein (ADR 0007). Nie direkt auf `main` committen.
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
src/Host/Rezeptverwaltung.Api     Komposition, Problem Details, OpenAPI
tests/                            Domain-, Application-, API-, Architekturtests
```

- Module greifen nicht aufeinander zu. Neue Module werden in `tests/Rezeptverwaltung.ArchitectureTests/ArchitekturTests.cs` → `Module` eingetragen.
- Fachregeln: `DomainRuleViolationException.Wenn(...)` = ungültige Eingabe → 422, `.ZustandWenn(...)` = Zustand lässt es nicht zu → 409, jeweils mit `code`. Eingaben vor dem Zustand prüfen. Codes sind stabil (`bereich.regel`, snake_case) (ADR 0009).
- Zahlen strikt: keine Zahlen als JSON-Strings; Gramm in Query-Parametern über den Typ `Gramm` binden, nie direkt als `decimal`, weil sonst "1,5" als 15 gelesen wird.
- Value Objects als `readonly record struct` oder `sealed record` mit Factory (`Von`, `Erfasst`) und Validierung. Aggregate sind `sealed`.
- Handler sind einfache Klassen, kein MediatR.

## Konventionen

- **Sprache:** Fachbegriffe deutsch (siehe [Glossar](docs/glossar.md)), technische Begriffe englisch (`Repository`, `Handler`, `Async`). Umlaute in Bezeichnern umschreiben (`ae`, `oe`, `ue`, `ss`).
- **Gewichte:** `decimal` in Gramm. Eingaben auf 0,01 g genau (`Gewicht.Erfasst`). Intern ungerundet rechnen, erst bei der Ausgabe runden.
- **Tests:** xUnit v3, Shouldly (nicht FluentAssertions), CsCheck für Eigenschaften. Testnamen als deutsche Sätze mit Unterstrichen. `TestContext.Current.CancellationToken` übergeben. Fachregeln mit `.ShouldHaveCode("…")` prüfen.
- Pakete zentral in `Directory.Packages.props` (Central Package Management).
