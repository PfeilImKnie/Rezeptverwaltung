# 0012 – Lokale Entwicklung mit Aspire; Container mit Podman oder Docker

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** Entwicklungsumgebung, Tests

## Kontext

Mit PostgreSQL braucht die API lokal eine Datenbank. Auf dem Entwicklungsrechner ist **Podman** (rootless)
installiert, Docker nicht. Integrationstests nutzen Testcontainers. Aspire 17 hat das Projektmodell geändert:
`Aspire.AppHost.Sdk` ist veraltet, Projekte werden über `AddDotnetProject(name, pfad)` eingebunden.

## Entscheidung

1. **Aspire AppHost** (`src/Host/Rezeptverwaltung.AppHost`) ist der Standardweg für lokale Entwicklung: `dotnet run --project src/Host/Rezeptverwaltung.AppHost`. Er startet:
   - PostgreSQL 18 (`18-alpine`, gleiche Hauptversion wie Tests und Betrieb) mit dem Volume `rezeptverwaltung-postgres-daten` und `ContainerLifetime.Persistent`,
   - die API, die die Verbindungszeichenfolge vom AppHost bekommt.
2. Der AppHost ist **nur** für die Entwicklung da. Die API kennt Aspire nicht und liest nur `ConnectionStrings:rezeptverwaltung`. Im Betrieb kommt diese aus der Umgebung (ADR 0005).
3. Aktuelles Projektmodell von Aspire: `Microsoft.NET.Sdk` mit den Paketen `Aspire.Hosting.AppHost`, `Aspire.Hosting.Dotnet` und `Aspire.Hosting.PostgreSQL`, außerdem `AddDotnetProject`.
4. **Container-Runtime: Podman oder Docker.** Aspire erkennt Podman automatisch, wenn kein Docker installiert ist (sonst `ASPIRE_CONTAINER_RUNTIME=podman`). Für Testcontainers mit Podman wird einmalig eingerichtet:
   - `systemctl --user enable --now podman.socket`
   - `~/.testcontainers.properties` mit `docker.host=unix://$XDG_RUNTIME_DIR/podman/podman.sock`

   Beides steht im README. In der CI wird Docker verwendet.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Docker Desktop / Docker Engine installieren | Standard für Testcontainers | Zweite Container-Runtime neben dem vorhandenen Podman; Root-Daemon |
| Nur `docker compose` / `podman compose` für die lokale DB | Nah am Betrieb | Kein Dashboard, Verbindungszeichenfolge von Hand pflegen |
| PostgreSQL lokal ohne Container installieren | Kein Container nötig | Version weicht leicht vom Betrieb ab, verschmutzt das System |
| **Aspire + vorhandenes Podman** | Ein Befehl, Dashboard, Daten bleiben erhalten, rootless | Aspire lädt beim ersten Start seine CLI (`dnx aspire.cli`) |

## Konsequenzen

- **Positiv:** Lokal genügt ein Befehl. Tests und Entwicklung laufen ohne Docker-Daemon und ohne Root-Rechte.
- **Negativ:** Podman braucht die zweistufige Einrichtung oben. Der Postgres-Container läuft nach dem Beenden weiter (gewollt, schnellerer Neustart) und muss bei Bedarf von Hand gestoppt werden.
- **Absicherung:** Die CI führt alle Datenbanktests mit Docker aus. Lokal mit Podman geprüft: Testcontainers inklusive Aufräumen, AppHost-Start, Daten bleiben über einen AppHost-Neustart erhalten.
