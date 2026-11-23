# 0006 – Teststrategie: alles automatisiert verifiziert

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** alle Projekte, CI

## Kontext

Die App soll vollständig durch automatische Tests verifiziert sein. Fehler in der Mengenberechnung haben
direkte Folgen in der Backstube: falsche Rezepturen und verschwendete Rohstoffe. Hohe Code-Coverage
allein beweist nicht, dass Tests etwas prüfen.

## Entscheidung

| Ebene | Projekt | Werkzeug | Zweck |
|---|---|---|---|
| Domain | `*.Domain.Tests` | xUnit v3, Shouldly, **CsCheck** (property-based) | Fachregeln, Grenzwerte, Rechen-Eigenschaften |
| Application | `*.Application.Tests` | xUnit v3, Fakes für Ports | Use-Case-Abläufe ohne Infrastruktur |
| Architektur | `ArchitectureTests` | **ArchUnitNET** | Erzwingt ADR 0002 |
| API | `Api.Tests` | `WebApplicationFactory` | Endpoints, Statuscodes, Problem Details, **Contract-Test** gegen `api/openapi.yaml` |
| Vertrag (laufend) | CI | **Schemathesis** | Generierte Anfragen gegen den Vertrag |
| Persistenz | `*.Infrastructure.Tests` (ab EF Core) | **Testcontainers** + PostgreSQL | Mapping und Migrationen gegen eine echte DB |
| Wirksamkeit | CI | **Stryker.NET** | Mutation Score der Domain **≥ 80 %**, sonst bricht die CI ab |
| E2E (ab Frontend) | – | Playwright for .NET | Wenige Kernabläufe |

Weitere Regeln:

- Plattform: **Microsoft Testing Platform** (`global.json`), Coverage mit `Microsoft.Testing.Extensions.CodeCoverage`.
- **Shouldly** statt FluentAssertions (seit v8 kommerziell).
- Testnamen sind deutsche Sätze mit Unterstrichen (`SkaliertAuf_veraendert_das_Rezept_nicht`).
- Fachregel-Tests prüfen den `code` **und** dass es eine verständliche Meldung gibt (`ShouldHaveCode`).
- Jeder Test erhält `TestContext.Current.CancellationToken`.
- **Unabhängiger Test:** Vor jedem PR prüft ein separater Agent ohne Kenntnis der Implementierungsüberlegungen die Änderung gegen die Anforderung (Skill `/pr-test`, ADR 0007).

## Betrachtete Alternativen

- **Nur Coverage-Schwelle:** Sie wird leicht durch Tests ohne echte Prüfung erfüllt. Deshalb verwenden wir den Mutation Score.
- **NUnit / MSTest:** gleichwertig. xUnit v3 ist in .NET verbreitet und unterstützt die Microsoft Testing Platform nativ.

## Konsequenzen

- **Positiv:** Hohe Sicherheit bei Refactorings, und Architekturregeln erodieren nicht.
- **Negativ:** Stryker verlängert die CI um etwa eine Minute pro Domain-Projekt.
- **Absicherung:** Die CI-Jobs `build-test`, `mutation` und `api-vertrag` sind Pflicht-Checks für den Merge.
