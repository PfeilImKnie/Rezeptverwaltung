# Rezeptverwaltung

Rezeptverwaltung für die Konditorei: Rezepte nach Gewicht, Grundrezepte als Bausteine, Skalierung,
Allergene, Kalkulation. API-first, damit Tablet-App, Alexa-Skill und weitere Frontends dieselbe API nutzen.

## Stand

Modul **Rezeptur**:

| Methode | Pfad | Zweck |
|---|---|---|
| `POST` | `/api/v1/rezepte` | Rezept mit Zutaten (in Gramm) anlegen |
| `GET` | `/api/v1/rezepte/{id}` | Rezept abrufen |
| `GET` | `/api/v1/rezepte/{id}/skaliert?zielgewichtGramm=…[&fassung=n]` | Alle Mengen auf ein Zielgewicht umrechnen |
| `GET` | `/api/v1/rezepte/{id}/skaliert/ring?durchmesserCm=…&hoeheCm=…` | Auf einen Ring umrechnen (über das Volumen) |
| `GET` | `/api/v1/rezepte/{id}/skaliert/blech?laengeCm=…&breiteCm=…&hoeheCm=…` | Auf ein Blech/einen Rahmen umrechnen |
| `GET` | `/api/v1/rezepte/{id}/fassungen/{n}` | Eine bestimmte Fassung abrufen |
| `POST` | `/api/v1/rezepte/{id}/entwurf` | Neuen Entwurf als Kopie der aktuellen Fassung anlegen |
| `PUT` | `/api/v1/rezepte/{id}/entwurf` | Inhalt des Entwurfs (Zutaten und Bezugsform) vollständig ersetzen; ohne `bezugsform` hat er danach keine Form |
| `DELETE` | `/api/v1/rezepte/{id}/entwurf` | Entwurf verwerfen |
| `POST` | `/api/v1/rezepte/{id}/entwurf/freigabe` | Entwurf freigeben |

Rezepte können eine **Bezugsform** haben (Ring oder Blech, ADR 0014) und lassen sich dann auf jede andere
Form umrechnen. Rezepte haben **Fassungen** mit Entwurf und Freigabe: Freigegebene Fassungen sind unveränderlich, Änderungen
entstehen als Entwurf und gelten erst nach der Freigabe (ADR 0013). Änderungen am Entwurf verlangen den Header
`If-Match` mit dem `ETag` der letzten Antwort; so überschreibt kein Gerät still die Änderung eines anderen.

Daten liegen in **PostgreSQL** (ADR 0008, 0011). Jede Anfrage braucht einen **Token**; jedes Rezept gehört einem
**Betrieb**, und Rechte richten sich nach der **Rolle** (Mitarbeiter/in, Konditor/in, Inhaber/in, ADR 0015).
Bis zum Hosting (ADR 0005) ist die API noch nicht öffentlich erreichbar.

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
erhalten), einen **Test-Login** auf `http://localhost:18080` und die API auf `http://localhost:5080`. Die API migriert
die Datenbank beim Start.

Token für den Betrieb `entwicklung` holen (Rolle: `inhaber`, `konditor` oder `mitarbeiter`):

```bash
TOKEN=$(curl -s -X POST http://localhost:18080/rezeptverwaltung/token \
  -d grant_type=client_credentials -d client_id=dev -d client_secret=dev -d scope=inhaber \
  | python3 -c "import sys, json; print(json.load(sys.stdin)['access_token'])")
curl -H "Authorization: Bearer $TOKEN" http://localhost:5080/api/v1/rezepte/<id>
```

Rezepte aus der Zeit vor den Betrieben gehören dem Betrieb `altbestand`. Lokal einem Betrieb zuordnen:
`UPDATE rezeptur.rezepte SET betrieb_id = 'entwicklung' WHERE betrieb_id = 'altbestand';`
Der Postgres-Container läuft nach dem Beenden weiter, damit der nächste Start schnell ist.
Stoppen (nur der Aspire-Container, nicht laufende Testcontainer; mit Docker `docker` statt `podman`):
`podman ps -q --filter name=^postgres- | xargs -r podman stop`.

Das Postgres-Passwort erzeugt Aspire einmalig und legt es in den User Secrets des AppHost ab. Gehen die User
Secrets verloren, passt das neue Passwort nicht mehr zum vorhandenen Volume. Dann das Volume löschen
(`podman volume rm rezeptverwaltung-postgres-daten`, **löscht die lokalen Daten**) oder das alte Passwort
wieder eintragen.

Beispiel (Token siehe oben):

```bash
curl -s -X POST localhost:5080/api/v1/rezepte -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
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
