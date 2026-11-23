# 0013 – Rezeptfassungen mit Entwurf und Freigabe; Schutz vor still verlorenen Änderungen

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** Modul Rezeptur (Domain, Application, Persistenz, API). Ergänzt [ADR 0009](0009-fehlerstatus-409-und-422-und-strikte-zahlen.md) um 412/428.

## Kontext

Fachliche Vorgaben der Konditormeisterin:

- Änderungen an einem Rezept dürfen nie stillschweigend wirken. Sie stellt **bewusst** auf eine neue Fassung um.
- Neue Fassungen beginnen als **Entwurf**, an dem sie ausprobieren kann, bevor er gilt.
- Später muss nachvollziehbar sein, wann welche Fassung wofür verwendet wurde (Zubereitung, folgt).

Technische Fakten:

- Die API wird von mehreren Geräten gleichzeitig genutzt (Tablet-PWA, Alexa, ADR 0004).
- Der unabhängige Test fand: Lädt das Tablet einen Entwurf, ändert Alexa ihn, und speichert das Tablet danach seinen alten Stand, geht Alexas Änderung **still verloren**. Die Prüfung über die DB-Systemspalte `xmin` allein erkennt nur Kollisionen innerhalb einer Anfrage.
- PostgreSQL kürzt Zeitpunkte in JSONB auf Mikrosekunden. Antwort und späterer Abruf zeigten so verschiedene Zeitpunkte.

## Entscheidung

1. **Fassungen:**
   - Ein `Rezept` (Identität und Name) besteht aus `Fassung`en 1…n.
   - Eine Fassung ist `Entwurf` oder `Freigegeben`. Freigegebene Fassungen sind unveränderlich, und es gibt **höchstens einen Entwurf**.
   - Ein neues Rezept beginnt mit Fassung 1 als Entwurf. Ein späterer Entwurf entsteht als Kopie der aktuellen (neuesten freigegebenen) Fassung.
   - Freigabe einer Fassung ohne Zutaten ist nicht möglich. Der einzige Entwurf kann nicht verworfen werden.
   - **Maßgebliche Fassung:** die aktuelle, solange es keine gibt, der Entwurf. Die bisherigen API-Felder (`zutaten`, `gesamtgewichtGramm`) und die Skalierung ohne Angabe beziehen sich darauf. Das hält `v1` abwärtskompatibel.
   - **Fassungsnummern werden nie wiederverwendet** (`LetzteFassungsnummer`). Sonst wäre eine Zubereitung mit „Fassung 2“ nach dem Verwerfen eines Entwurfs mehrdeutig.
   - Erstellungs- und Freigabezeitpunkt kommen aus `TimeProvider`.
2. **Speicherung:**
   - Die Fassungen liegen als JSONB-Array im Rezept-Datensatz (ADR 0011), mit festen Feldnamen (`nummer`, `status`, `erstelltAm`, `freigegebenAm`, `zutaten`).
   - Zeitpunkte werden als Text im Roundtrip-Format „O“ gespeichert, also verlustfrei mit 100-ns-Ticks.
   - Jede Fassung besitzt eigene Instanzen ihrer Zutatenzeilen.
   - **Datenmigration:** Bestehende Rezepte werden zu *freigegebener* Fassung 1, weil sie bereits in Gebrauch waren. **Ausnahme:** Rezepte ohne Zutaten werden zu Fassung 1 als *Entwurf*, weil eine Fassung ohne Zutaten nicht freigegeben sein darf. Erstellungs- und Freigabezeitpunkt sind dabei der Zeitpunkt der Migration, denn ein früheres Datum war nicht gespeichert.
3. **Schutz vor verlorenen Änderungen:**
   - Jede Antwort zu einem Rezept trägt einen **`ETag`** (Stand des Rezepts, aus `xmin`).
   - Änderungen am Entwurf (anlegen, bearbeiten, verwerfen, freigeben) **verlangen `If-Match`**:
     - fehlt der Header → **428** `anfrage.if_match_fehlt`,
     - passt er nicht → **412** `rezept.veraltet`. Es gilt der starke Vergleich: Nur ein einzelnes ETag in Anführungszeichen kann passen. Werte ohne Anführungszeichen, `*`, schwache ETags und Listen passen nie. Ein fehlender oder leerer Header zählt als fehlend.
   - Eine Fassungsnummer kleiner als 1 ergibt im Pfad (`fassungen/0`) 404, weil es diese Ressource nicht gibt, und in der Query (`?fassung=0`) 400, weil der Parameter ungültig ist.
   - Ändert eine andere Anfrage das Rezept zwischen Stand-Prüfung und Speichern, erkennt das die Datenbank (`xmin`). Dann gibt es **409** `rezept.gleichzeitig_geaendert`.
   - Reihenfolge der Prüfungen: 404 → 428/412 → 422 → 409.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Änderungen direkt am Rezept, Historie als Protokoll | Einfacher | Keine „bewusste Umstellung“; Grundrezepte änderten sich still mit |
| Jede Speicherung sofort neue Fassung, ohne Entwurf | Kein Zustand „Entwurf“ | Ausprobieren unmöglich; jede Zwischenspeicherung würde zur geltenden Fassung |
| Fassungen als eigene Tabelle | Einzeln abfragbar | Aggregat über Tabellen verteilt; Reihenfolge von Hand pflegen (vgl. ADR 0011) |
| Versionsfeld im Request-Body statt ETag/If-Match | Für Clients sichtbarer | Eigenerfindung statt HTTP-Standard (RFC 9110); generierte Clients unterstützen ETags |
| If-Match optional | Bequemer für Clients | Genau der still verlorene Fall bliebe möglich |
| 409 statt 412 bei veraltetem Stand | Ein Statuscode weniger | 412 ist die Standardantwort auf eine fehlgeschlagene Vorbedingung; Clients unterscheiden „neu laden“ (412) von „Zustand passt nicht“ (409) |

## Konsequenzen

- **Positiv:**
  - Grundrezepte ändern sich nie still.
  - Ältere Fassungen bleiben abrufbar und skalierbar, also auch die Fassung zum Probebacken.
  - Kein Gerät überschreibt still ein anderes.
- **Negativ:**
  - Clients müssen den ETag merken und mitsenden.
  - Nach 412 lädt der Client neu, und der Mensch entscheidet, wie es weitergeht.
  - Die Rückwärts-Migration (`Down`) verwirft die Fassungshistorie. Übrig bleibt die maßgebliche Fassung, nie ein unveröffentlichter Entwurf.
- **Absicherung:**
  - `FassungenTests` und `FassungenGrenzfallTests` (Domain, property-based über zufällige Arbeitsabläufe) prüfen: Freigegebenes bleibt unverändert, höchstens ein Entwurf, Nummern eindeutig und nie wiederverwendet.
  - `DatenmigrationTests` migrieren alte Daten wirklich.
  - `NebenlaeufigkeitTests` und `FassungenPersistenzGrenzfallTests` prüfen gleichzeitige Speichervorgänge und Zeitpunkte bis auf den Tick.
  - `StandApiTests` decken das Szenario „Tablet lädt, Alexa ändert, Tablet speichert alt“ ab, außerdem 428, `*` und schwache ETags.
  - Schemathesis (`schemathesis.toml`) wertet 412 auf beliebige If-Match-Werte als erwartete Antwort.
