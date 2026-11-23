# 0008 – Persistenz mit PostgreSQL und EF Core, In-Memory als Übergang

- **Status:** Akzeptiert, umgesetzt; Details in [0011](0011-abbildung-der-aggregate-in-postgresql.md)
- **Datum:** 2026-11-23
- **Betrifft:** Infrastructure aller Module

## Kontext

Das Grundgerüst soll ohne Datenbank lauffähig und testbar sein. Langfristig brauchen wir eine verlässliche,
sicherbare Datenhaltung. Der Identity Provider (ADR 0004) nutzt ohnehin PostgreSQL.

## Entscheidung

- **Ziel:** PostgreSQL mit EF Core (Npgsql), **ein Schema pro Modul** (z. B. `rezeptur`), Migrationen pro Modul.
- Das Mapping von Value Objects (Complex Types oder Owned Types) liegt ausschließlich in `Infrastructure`. Die Domain bleibt frei von EF-Attributen.
- Rezeptversionen (folgt) werden als **unveränderliche Snapshots** gespeichert. Event Sourcing ist nicht nötig.
- **Übergang:** `InMemoryRezeptRepository`. Daten gehen beim Neustart verloren, deshalb wird nichts produktiv betrieben, bevor diese ADR umgesetzt ist.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Event Sourcing | Vollständige Historie | Hohe Komplexität; Versionierung geht auch mit Snapshots |
| Dokumenten-DB (Marten auf Postgres) | Aggregate als JSON, wenig Mapping | Zusätzliche Abhängigkeit, schwächere Abfragen über Module |
| SQLite | Kein DB-Server | Schwächer bei Nebenläufigkeit; Postgres läuft für den IdP ohnehin |
| **PostgreSQL + EF Core** | Standard, gut testbar mit Testcontainers | Mapping-Aufwand für Value Objects |

## Konsequenzen

- **Positiv:** Ein Datenbankserver für alles, ein Backup-Verfahren.
- **Negativ:** Integrationstests brauchen Docker (lokal und in der CI).
- **Absicherung:** Integrationstests mit Testcontainers sind ab Umsetzung Teil der Definition of Done.
