---
name: pr-test
description: Unabhängiger Test der aktuellen Änderungen. Ein separater Tester-Agent leitet Testfälle aus der Anforderung ab (zuerst Blackbox, dann Abgleich mit dem Code), ergänzt fehlende Tests ausschließlich unter tests/, führt alle Tests und Mutation Testing (Stryker) aus und meldet Fehler, ohne Produktionscode zu ändern. Pflicht vor jedem Pull Request (ADR 0007); wird von /pr-erstellen aufgerufen.
argument-hint: "<Anforderung / Akzeptanzkriterien> [--basis <branch>]"
context: fork
agent: tester
---

# Unabhängiger Test

Argumente: `$ARGUMENTS`. Das ist die **Anforderung** (Akzeptanzkriterien). Optional gibt `--basis <branch>` die Basis an, Standard ist `main`.

## 1. Anforderung klären

Wenn keine Anforderung übergeben wurde, leite sie aus `git log <basis>..HEAD` und dem Branch-Namen ab und
schreibe in den Bericht: **„Anforderung abgeleitet, nicht vorgegeben, Unabhängigkeit eingeschränkt.“**

## 2. Testfälle zuerst aus der Anforderung ableiten (Blackbox)

Lies dafür nur `docs/glossar.md`, `docs/definition-of-done.md`, `docs/adr/0006-teststrategie.md` und
bei API-Bezug `api/openapi.yaml`. **Lies noch keinen Produktionscode.**

Lege eine Testfallliste an:
- Äquivalenzklassen und **Grenzwerte** (min, min−ε, max, max+ε, 0, leer, Genauigkeitsgrenze 0,01 g),
- Fehlerfälle mit erwartetem Fehlercode bzw. HTTP-Status,
- **Eigenschaften** für property-based Tests (Invarianten, z. B. Summe = Zielgewicht, Verhältnisse bleiben gleich, Hin- und Rückweg),
- fachliche Szenarien aus der Backstube (z. B. Sachertorte von Ø 26 auf Ø 18 umrechnen).

## 3. Abgleich mit Implementierung und vorhandenen Tests

```bash
git diff <basis> --stat && git diff <basis>
git status --porcelain
```

Ordne jedem Testfall den vorhandenen Test zu oder markiere ihn als **Lücke**. Achte auch auf Verhalten im Code,
das die Anforderung nicht verlangt (ungeplante Funktion, eventuell ungetestet).

## 4. Lücken schließen (nur unter `tests/`)

Halte dich an die Konventionen der vorhandenen Tests:
- xUnit v3, Shouldly, CsCheck. Testnamen sind deutsche Sätze mit Unterstrichen.
- Fachregeln mit `.ShouldHaveCode("…")` prüfen.
- `TestContext.Current.CancellationToken` verwenden.
- Domain-Tests ohne Infrastruktur, Application-Tests mit Fakes, API-Tests über `ApiFactory`.

Jeden neuen Test einmal gezielt ausführen (`dotnet test --project <proj> --filter-method "*Name*"`, bei
Bedarf `-- --help` für die Filtersyntax). Deckt ein Test einen Fehler auf: **Test rot lassen** und als Fehler
melden.

## 5. Vollständig ausführen

```bash
dotnet format Rezeptverwaltung.slnx --verify-no-changes
dotnet test --solution Rezeptverwaltung.slnx --coverage --coverage-output-format cobertura
```

**Mutation Testing** für jedes geänderte Domain-Projekt (Schwelle 80 %):

```bash
cd tests/<Modul>.Domain.Tests && dotnet tool restore && dotnet stryker
```

Überlebende Mutanten aus `StrykerOutput/<neuester>/reports/` auswerten. Für fachlich relevante Mutanten
schreibst du Tests. Äquivalente Mutanten (fachlich nicht unterscheidbar) listest du mit Begründung auf.

Bei API-Änderungen: Contract-Test muss grün sein. Ist `st` (Schemathesis) lokal verfügbar, führe es gegen
die laufende API aus (`st run api/openapi.yaml --url http://127.0.0.1:5080 --checks all`). Sonst steht im
Bericht: „Schemathesis nur in CI“.

## 6. Bericht (genau dieses Format, es wird in die PR-Beschreibung übernommen)

```markdown
### Unabhängiger Test

**Ergebnis: Bestanden** | **Ergebnis: Fehler gefunden**
Anforderung: vorgegeben / abgeleitet · Tests gesamt: N (neu: M) · Mutation Score Domain: X %

**Testfälle aus der Anforderung**
| Testfall | Art | Abgedeckt durch | Status |
|---|---|---|---|
| Zielgewicht 0 g wird abgelehnt (422) | Grenzwert | `RezepteApiTests.…` (neu) | ✅ |

**Gefundene Fehler**
| # | Test (rot) | Erwartet | Tatsächlich |
|---|---|---|---|

**Neue / geänderte Testdateien:** …
**Überlebende Mutanten (äquivalent):** …
**Nicht geprüft / Einschränkungen:** …
```
