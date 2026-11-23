# 0011 – Abbildung der Aggregate in PostgreSQL

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** Infrastructure aller Module, API-Host, API-Vertrag. Konkretisiert [ADR 0008](0008-persistenz-postgresql-mit-ef-core.md).

## Kontext

ADR 0008 legt PostgreSQL mit EF Core fest, ein Schema pro Modul. Offen war, **wie** Aggregate abgebildet
werden und wann Migrationen laufen. Die Fakten:

- Die Reihenfolge der Zutaten ist fachlich bedeutsam (Arbeitsreihenfolge, Zucker für Teig vs. Eischnee). Relationale Tabellen garantieren keine Reihenfolge.
- Ein Rezept wird immer als Ganzes geladen und gespeichert (Aggregat).
- Die App läuft als **eine** Instanz (ADR 0005).
- Der unabhängige Test hat gezeigt: PostgreSQL kann das Nullzeichen U+0000 weder in `text` noch in `jsonb` speichern. Ein Name mit U+0000 führte zu HTTP 500, obwohl Vertrag und Domain ihn erlaubten.

## Entscheidung

1. **Ein Datensatz pro Aggregat.** Listen innerhalb eines Aggregats (z. B. die Zutaten) liegen als **JSONB-Array** im Datensatz. JSON-Arrays erhalten die Reihenfolge.
2. **Mapping nur in `Infrastructure/Persistence`:**
   - Value Objects werden über `HasConversion` mit den Domain-Factories abgebildet (`RezeptName.Von`, `Gewicht.Erfasst`). Die Domain bleibt EF-frei.
   - Die Feldnamen im JSON (`bezeichnung`, `gramm`) sind ein Vertrag mit künftigen Migrationen und durch einen Test fixiert.
3. **Schema pro Modul** (`rezeptur`), eigene Migrations-Historie im Modulschema (`__ef_migrationen`).
4. **Migrationen beim Start der API** (`MigriereRezepturDatenbankAsync`). Abschaltbar über `Persistenz:MigrationenBeimStart=false`, falls später mehrere Instanzen oder getrennte Migrationsläufe nötig werden.
5. **Ohne Verbindungszeichenfolge** (`ConnectionStrings:rezeptverwaltung` fehlt oder ist leer) bricht die API beim Start mit einer verständlichen Meldung ab. Es gibt keinen stillen Rückfall auf einen Speicher im RAM, das In-Memory-Repository entfällt.
6. **U+0000 ist in Freitext unzulässig** (Domain: `rezept.name_nullzeichen`, `zutat.bezeichnung_nullzeichen` → 422; Vertrag: Muster ohne U+0000). Das ist eine bewusste Einschränkung des Vertrags innerhalb von `v1`. Es gibt noch keine Clients, und kein Rezeptname braucht ein Nullzeichen.
7. Der Health-Check `/health` prüft die Datenbankverbindung.
8. **Datenbanktests immer gegen echtes PostgreSQL** in derselben Hauptversion wie im Betrieb (18), per Testcontainers. Kein EF-InMemory und kein SQLite. Die API-Tests laufen Ende-zu-Ende über die Datenbank.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Zutaten als eigene Tabelle mit Positionsspalte | Relational abfragbar | Reihenfolge muss von Hand gepflegt werden; mehr Mapping; Aggregat über zwei Tabellen verteilt |
| Ganzes Aggregat als ein JSON-Dokument | Kein Mapping | Name und Id nicht als Spalten indizierbar; schwächere Abfragen |
| Migrationen nur per Bundle in der Deployment-Pipeline | Kontrollierter | Zusätzlicher Deploy-Schritt; bei einer Instanz ohne Mehrwert |
| U+0000 in der Persistenz umkodieren | Vertrag bleibt unverändert | Ersatzzeichen mehrdeutig; versteckte Sonderlogik für einen fachlich sinnlosen Fall |
| **Spalten für Identität/Name, JSONB für Listen; Migration beim Start; U+0000 unzulässig** | Reihenfolge sicher, Aggregat = ein Datensatz, einfacher Betrieb | JSONB-Inhalte sind schwerer zu migrieren als Spalten |

## Konsequenzen

- **Positiv:** Speichern und Laden sind eine Zeile pro Rezept, und die Reihenfolge bleibt garantiert erhalten. Es gibt keinen separaten Migrationsschritt im Betrieb.
- **Negativ:** Ändert sich die Struktur der Zutatenzeilen, braucht es eine Datenmigration im JSON. Die Factories validieren auch beim Laden. Wird eine Fachregel verschärft (z. B. kürzere Maximallänge), ohne die Altdaten per Migration anzupassen, lädt das betroffene Rezept nicht mehr, und `GET` liefert dann 422, obwohl der Client nichts eingegeben hat. **Jede Verschärfung einer Fachregel braucht deshalb eine Datenmigration oder eine Prüfung der Bestandsdaten.**
- **Absicherung:**
  - `MigrationsTests.Modell_und_Migrationen_stimmen_ueberein` lässt die Tests und damit die CI scheitern, wenn eine Migration fehlt.
  - `EfRezeptRepositoryTests` und `PersistenzGrenzfallTests` prüfen per Property-Test die Abbildung beim Speichern und Laden, außerdem Reihenfolge, Duplikate, Genauigkeit, Unicode und das JSON-Format.
  - `PersistenzApiTests` prüfen Neustart, Migration beim Start, Abschalter und die fehlende bzw. leere Verbindungszeichenfolge.
  - Die Architekturregel nimmt nur den Namensraum `*.Persistence.Migrations` (generiert, zwingend öffentlich) von „nur `<Modul>Module` öffentlich“ aus.
