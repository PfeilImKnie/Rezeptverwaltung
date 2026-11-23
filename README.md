# Rezeptverwaltung

Rezeptverwaltung für die Konditorei: Rezepte nach Gewicht, Grundrezepte als Bausteine, Skalierung,
Allergene, Kalkulation. API-first, damit Tablet-App, Alexa-Skill und weitere Frontends dieselbe API nutzen.

## Stand

Grundgerüst mit erstem fachlichem Schnitt im Modul **Rezeptur**:

| Methode | Pfad | Zweck |
|---|---|---|
| `POST` | `/api/v1/rezepte` | Rezept mit Zutaten (in Gramm) anlegen |
| `GET` | `/api/v1/rezepte/{id}` | Rezept abrufen |
| `GET` | `/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=…` | Alle Mengen auf ein Zielgewicht umrechnen |

Daten liegen vorerst **nur im Speicher** (ADR 0008). Authentifizierung folgt (ADR 0004). Die API ist daher
noch nicht für den öffentlichen Betrieb gedacht.

## Voraussetzungen

- .NET SDK 10.0.100 oder neuer (`global.json`)
- Docker: erst für Integrationstests mit PostgreSQL (folgt)

## Loslegen

```bash
dotnet build Rezeptverwaltung.slnx
dotnet test --solution Rezeptverwaltung.slnx
dotnet run --project src/Host/Rezeptverwaltung.Api
```

Beispiel:

```bash
curl -s -X POST localhost:5080/api/v1/rezepte -H 'Content-Type: application/json' \
  -d '{"name":"Mürbeteig","zutaten":[{"bezeichnung":"Zucker","gramm":100},{"bezeichnung":"Butter","gramm":200},{"bezeichnung":"Mehl","gramm":300}]}'
```

## Qualität

| Prüfung | Werkzeug | Wo |
|---|---|---|
| Domain-, Use-Case- und API-Tests | xUnit v3, Shouldly, CsCheck | lokal + CI |
| Architekturregeln | ArchUnitNET | lokal + CI |
| API-Vertrag | Contract-Test + Schemathesis | lokal / CI |
| Wirksamkeit der Tests | Stryker.NET (≥ 80 %) | lokal + CI |
| Format, Analyzer, verwundbare Pakete | `dotnet format`, .NET-Analyzer, NuGet-Audit | lokal + CI |

## Dokumentation

- [Definition of Done](docs/definition-of-done.md)
- [Architekturentscheidungen (ADR)](docs/adr/README.md)
- [Glossar](docs/glossar.md)
- [API-Vertrag](api/openapi.yaml)

## Arbeitsweise

Jeder Pull Request enthält einen unabhängigen Test, ein unabhängiges Code-Review und die Dokumentation
(ADR 0007). Mit Claude Code: `/pr-erstellen`. Einzelne Schritte: `/pr-test`, `/pr-review`, `/adr`.
