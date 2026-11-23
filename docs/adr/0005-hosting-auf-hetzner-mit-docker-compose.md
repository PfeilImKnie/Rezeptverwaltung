# 0005 – Hosting auf einer Hetzner-Cloud-VM mit Docker Compose

- **Status:** Akzeptiert (Umsetzung ausstehend)
- **Datum:** 2026-11-23
- **Betrifft:** Betrieb, Infrastruktur

## Kontext

Wenige Nutzer, ein Betreiber, begrenztes Budget. Die App muss für Alexa öffentlich per HTTPS erreichbar
sein (ADR 0004). Daten sollen in Deutschland liegen (DSGVO).

## Entscheidung

- **Eine Hetzner-Cloud-VM** (kleine Shared-vCPU-Instanz, ca. 4 GB RAM, Standort Deutschland).
- **Docker Compose** mit `caddy` (Reverse Proxy, automatisches TLS), `api`, `alexa` (sobald vorhanden), `zitadel` und `postgres`.
- **Netz:** Die Hetzner Cloud Firewall lässt öffentlich nur 80/443 zu. SSH ist nur per WireGuard/Tailscale oder von einer festen IP erreichbar. PostgreSQL ist nie öffentlich.
- **Härtung:** Container laufen als Nicht-Root mit schreibgeschütztem Dateisystem, `unattended-upgrades`, Rate Limiting in der API, Security-Header über Caddy.
- **Backup:** Täglich `pg_dump` und Bilder, mit restic verschlüsselt auf eine Hetzner Storage Box an einem **anderen Standort**. Die Wiederherstellung wird regelmäßig getestet.
- **Kein Kubernetes** und kein Message Broker.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Heimserver / NAS | Keine laufenden Kosten | Öffentliche Erreichbarkeit für Alexa über den Heimanschluss, Verfügbarkeit |
| Kubernetes (k3s) | Skalierung, deklarativ | Betriebsaufwand weit über Bedarf |
| PaaS (Azure App Service o. ä.) | Kein OS-Betrieb | Kosten, IdP und Postgres zusätzlich |
| **Hetzner-VM + Compose** | Günstig, einfach, DSGVO | OS-Patches liegen bei uns |

## Konsequenzen

- **Positiv:** Niedrige Kosten, verständliches Setup, das lokal mit .NET Aspire oder Compose nachgebaut werden kann.
- **Negativ:** Ein Single Point of Failure. Für den Einsatzzweck ist das akzeptabel, Backups mindern das Risiko.
- **Absicherung:** Ein eigener PR mit Dockerfile, Compose-Datei und Runbook. Der Backup-Restore-Test wird dokumentiert.
