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

Daten liegen in **PostgreSQL** (ADR 0008, 0011). Authentifizierung folgt (ADR 0004). Die API ist daher
noch nicht für den öffentlichen Betrieb gedacht.

## Voraussetzungen

- .NET SDK 10.0.100 oder neuer (`global.json`)
- **Podman oder Docker** für PostgreSQL (lokal über Aspire, in Tests über Testcontainers)

### Einmalig mit Podman (ADR 0012)

```bash
systemctl --user enable --now podman.socket          # Docker-kompatibler Socket
echo "docker.host=unix://$XDG_RUNTIME_DIR/podman/podman.sock" > ~/.testcontainers.properties
```

Aspire erkennt Podman automatisch, wenn kein Docker installiert ist (sonst `ASPIRE_CONTAINER_RUNTIME=podman`).
Mit Docker ist nichts weiter einzurichten.

## Loslegen

```bash
dotnet build Rezeptverwaltung.slnx
dotnet test --solution Rezeptverwaltung.slnx                 # startet PostgreSQL-Container selbst
dotnet run --project src/Host/Rezeptverwaltung.AppHost       # Aspire: PostgreSQL + API + Dashboard
```

Der AppHost startet PostgreSQL 18 im Container mit dem Volume `rezeptverwaltung-postgres-daten` (Daten bleiben
erhalten) und die API auf `http://localhost:5080`. Die API migriert die Datenbank beim Start.
Der Postgres-Container läuft nach dem Beenden weiter, damit der nächste Start schnell ist.
Stoppen (nur der Aspire-Container, nicht laufende Testcontainer; mit Docker `docker` statt `podman`):
`podman ps -q --filter name=^postgres- | xargs -r podman stop`.

Das Postgres-Passwort erzeugt Aspire einmalig und legt es in den User Secrets des AppHost ab. Gehen die User
Secrets verloren, passt das neue Passwort nicht mehr zum vorhandenen Volume. Dann das Volume löschen
(`podman volume rm rezeptverwaltung-postgres-daten`, **löscht die lokalen Daten**) oder das alte Passwort
wieder eintragen.

Beispiel:

```bash
curl -s -X POST localhost:5080/api/v1/rezepte -H 'Content-Type: application/json' \
  -d '{"name":"Mürbeteig","zutaten":[{"bezeichnung":"Zucker","gramm":100},{"bezeichnung":"Butter","gramm":200},{"bezeichnung":"Mehl","gramm":300}]}'
```

## Qualität

| Prüfung | Werkzeug | Wo |
|---|---|---|
| Domain-, Use-Case- und API-Tests | xUnit v3, Shouldly, CsCheck | lokal + CI |
| Persistenz gegen echtes PostgreSQL 18 | Testcontainers | lokal + CI |
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
