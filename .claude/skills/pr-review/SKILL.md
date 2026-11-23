---
name: pr-review
description: Unabhängiges Code-Review der aktuellen Änderungen (Branch gegen main, inkl. nicht committeter Dateien) gegen Definition of Done, ADRs, DDD- und Sicherheitsregeln. Pflicht vor jedem Pull Request (ADR 0007); wird von /pr-erstellen als letzter Prüfschritt aufgerufen, kann aber auch einzeln laufen. Liefert belegte Befunde nach Schwere und das Ergebnis "Freigegeben" oder "Änderungen nötig".
argument-hint: "[Basis-Branch=main] [Anforderung in einem Satz]"
context: fork
agent: reviewer
---

# Unabhängiges Code-Review

Argumente: `$ARGUMENTS` (erstes Wort = Basis-Branch, falls es ein Branch ist; sonst `main`. Der Rest ist die Anforderung).

## 1. Umfang feststellen

```bash
git status --porcelain
git diff <basis> --stat            # committete und nicht committete Änderungen gegen die Basis
git diff <basis>                   # vollständiger Diff
git log <basis>..HEAD --oneline
```

Neue, noch nicht versionierte Dateien (`??` in `git status`) liest du vollständig. Den Diff liest du ganz.
Bei jeder geänderten Methode liest du die umgebende Datei, nicht nur den Hunk.

## 2. Maßstab laden

Lies: `CLAUDE.md`, `docs/definition-of-done.md`, `docs/adr/README.md` und jede ADR, die die
geänderten Bereiche betrifft, `docs/glossar.md`. Bei API-Änderungen außerdem `api/openapi.yaml`.

## 3. Mechanisch prüfen

```bash
dotnet format Rezeptverwaltung.slnx --verify-no-changes
dotnet build Rezeptverwaltung.slnx
dotnet test --solution Rezeptverwaltung.slnx
```

Ein roter Schritt ist automatisch ein **Blocker**. Gib die relevante Ausgabe wörtlich an.

## 4. Inhaltlich prüfen

Gehe jede Kategorie durch. Für jede Kategorie gibt es entweder Befunde oder ausdrücklich „ohne Befund“.

1. **Korrektheit:** Verfolge die Logik mit konkreten Werten. Randfälle: 0, negativ, Maximum, Rundung (decimal, `MidpointRounding`), leere Listen, `null`, Duplikate, Nebenläufigkeit. Stimmt das Ergebnis fachlich für die Backstube?
2. **Domain / DDD:** Liegen Invarianten im Aggregat oder Value Object (nicht in Handler oder Endpoint)? Wird die Ubiquitous Language aus dem Glossar verwendet? Sind Aggregate `sealed`, Value Objects unveränderlich? Werden Aggregate nur über ihre Methoden verändert? Haben Fachregeln einen stabilen `code`?
3. **Architektur (ADR 0002):** Schichtrichtung Domain ← Application ← Infrastructure, keine Zugriffe zwischen Modulen, öffentlich ist nur `<Modul>Module`. Neues Modul in `ArchitekturTests.Module` eingetragen?
4. **API-Vertrag (ADR 0003):** Wurde `api/openapi.yaml` angepasst? Stimmen die Einschränkungen in Vertrag und Domain **genau** überein (Länge, Bereich, Genauigkeit, Pflichtfelder)? Statuscodes 400/404/422 korrekt? Ist die Änderung innerhalb von `v1` abwärtskompatibel?
5. **Sicherheit:** Eingaben begrenzt, keine Secrets, keine Tokens oder personenbezogenen Daten in Logs, Fehlermeldungen ohne interne Details. Neue Abhängigkeiten begründet? Ab ADR-0004-Umsetzung: AuthN/AuthZ an jedem Endpoint.
6. **Tests (ADR 0006):** Würden die Tests fehlschlagen, wenn der Code falsch wäre? Gibt es Grenzwerte, Fehlerfälle, property-based Tests für Rechenlogik? API-Tests für jeden Statuscode des Vertrags? Wird der Bericht von `/pr-test` geliefert, prüfe ihn auf Plausibilität.
7. **Dokumentation (ADR 0001):** Braucht die Änderung eine ADR, und gibt es sie? Sind Glossar, README und CLAUDE.md aktuell? Stimmen Kommentare und XML-Doku mit dem Code überein?
8. **Einfachheit:** Unnötige Abstraktionen, toter Code, Duplikate, unklare Namen.

## 5. Befunde verifizieren

Prüfe jeden Befund, bevor du ihn aufschreibst: Stelle an der Codestelle sicher, dass das Szenario wirklich
eintritt. Wenn möglich, belege ihn mit einem Test oder einem Befehl, ohne Dateien anzulegen (z. B. durch
Lesen eines bestehenden Tests oder `dotnet test --filter`). Was sich nicht belegen lässt, wird zur Frage.

Schweregrade:
- **Blocker:** falsches fachliches Ergebnis, Datenverlust, Sicherheitslücke, roter Build oder Test, verletzte Pflicht der DoD.
- **Major:** Fehler in Randfällen, Architekturverstoß, fehlende Tests für eine Fachregel, Vertrag und Domain inkonsistent, fehlende ADR.
- **Minor:** Lesbarkeit, Benennung, kleinere Vereinfachung, unvollständige Doku.
- **Hinweis:** Geschmack oder Ideen. Muss nicht umgesetzt werden.

## 6. Bericht (genau dieses Format, es wird in die PR-Beschreibung übernommen)

```markdown
### Code-Review

**Ergebnis: Freigegeben** | **Ergebnis: Änderungen nötig**
Basis: `<basis>` · Geprüfte Dateien: <Anzahl> · Build/Format/Tests: ✅/❌

| # | Schwere | Datei:Zeile | Befund | Szenario | Vorschlag |
|---|---|---|---|---|---|
| 1 | Major | `src/…/Rezept.cs:42` | … | Eingabe … → … | … |

**Kategorien ohne Befund:** Korrektheit, …
**Fragen an die Autorin / den Autor:** …
```

„Freigegeben“ heißt: keine Blocker und keine Major-Befunde.
