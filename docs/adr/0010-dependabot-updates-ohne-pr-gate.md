# 0010 – Reine Dependabot-Updates sind vom PR-Gate ausgenommen

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** Arbeitsweise, CI. Ergänzt [ADR 0007](0007-pr-workflow-test-review-doku.md).

## Kontext

ADR 0007 verlangt für jeden PR einen unabhängigen Test, ein Code-Review und Dokumentation. Der CI-Job
`pr-gate` prüft das anhand der PR-Beschreibung, und der Branch-Schutz macht ihn zur Pflicht.

Dependabot erstellt wöchentlich PRs für NuGet-Pakete und GitHub Actions (`.github/dependabot.yml`). Diese PRs
haben keine solche Beschreibung und scheitern deshalb immer am Gate. Schon am ersten Tag waren es vier PRs.

Ein reines Versions-Update ändert keinen eigenen Code. Was dabei schiefgehen kann (Kompilierfehler,
geändertes Verhalten, Schwachstellen, Bruch des API-Vertrags), prüfen die übrigen Pflicht-Checks automatisch:

- Build mit Analyzern und Warnungen als Fehler,
- alle Tests,
- Mutation Testing,
- Schemathesis,
- NuGet-Audit.

## Entscheidung

1. Der Job `pr-gate` wird für einen PR übersprungen, wenn **beide** Bedingungen gelten:
   - Der PR wurde von `dependabot[bot]` erstellt (`github.event.pull_request.user.login`).
   - Der aktuelle Lauf wurde von `dependabot[bot]` ausgelöst (`github.actor`).
2. Alle anderen Pflicht-Checks (`build-test`, `mutation`, `api-vertrag`) gelten unverändert.
3. **Ein menschlicher Commit auf den Dependabot-Branch** (z. B. Code-Anpassungen nach einem Major-Update) **beendet die Ausnahme dauerhaft.** Dann gilt das volle Verfahren nach ADR 0007, und der PR läuft über `/pr-erstellen`. Dependabot fasst Branches mit fremden Commits nicht mehr an, deshalb kommen alle weiteren Läufe von Menschen.
   Eine **geänderte PR-Beschreibung** löst das Gate nur für diesen einen Lauf aus. Ein späterer Dependabot-Rebase überspringt es wieder. Das ist so gewollt: Am Code ändert sich nichts, und das Gate prüft Test, Review und Doku von Code.
4. Ein Dependabot-PR wird erst gemergt, wenn alle übrigen Checks grün sind. Bei einem Major-Update überfliegt der Mensch vor dem Merge die Release Notes. Brechende Änderungen, die die Tests nicht abdecken, werden so bemerkt.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Volles Verfahren für jedes Update | Maximal gründlich | Wöchentlich mehrere Agenten-Läufe ohne fachlichen Inhalt; Updates bleiben liegen, Sicherheitslücken länger offen |
| Gate prüft nur den PR-Autor | Einfach | Menschliche Commits auf einem Dependabot-Branch würden das Gate unbemerkt umgehen |
| Ausnahme nur für Patch- und Minor-Updates | Strenger bei Majors | Die Update-Art steht nicht zuverlässig im Event; bei Majors greift über Punkt 3 ohnehin das volle Verfahren, sobald Code angepasst werden muss |
| **Autor und Auslöser sind Dependabot** | Kein Aufwand bei reinen Updates; jeder menschliche Eingriff fällt unter das Gate | Unverändert gebliebene Major-Updates brauchen einen Blick in die Release Notes (Punkt 4) |

## Konsequenzen

- **Positiv:** Sicherheits- und Versions-Updates lassen sich ohne Aufwand mergen, sobald die CI grün ist. Die Prüfung von Code, der von Menschen oder Agenten geschrieben wurde, bleibt unverändert streng.
- **Negativ:** Für reine Updates gibt es kein unabhängiges Review. Das ist vertretbar, weil sie keinen eigenen Code enthalten.
- **Restrisiko:** Für den Branch-Schutz zählt der letzte Lauf auf dem Head-Commit. Löst Dependabot nach einem menschlichen Commit auf demselben Commit noch einen Lauf aus, würde das Gate übersprungen. Das ist praktisch ausgeschlossen: Dependabot fasst PRs mit fremden Commits nicht mehr an, und `@dependabot recreate` verwirft die menschlichen Commits. Trotzdem gilt: Hat ein Mensch auf einen Dependabot-Branch committet, wird vor dem Merge geprüft, dass `PR-Gate` tatsächlich **gelaufen** ist und nicht übersprungen wurde.
- Eine von Hand geänderte Beschreibung kann nach einem späteren Dependabot-Rebase ungeprüft als Squash-Commit-Nachricht in `main` landen (`PR_BODY`). Das ist vertretbar, weil es nur Text ist und kein Code.
- Re-Runs behalten den ursprünglichen Auslöser. Startet ein Mensch einen Dependabot-Lauf neu, bleibt das Gate übersprungen. Das ist korrekt, weil sich der Inhalt nicht ändert.
- **Absicherung:**
  - Die Bedingung steht kommentiert in `.github/workflows/ci.yml` beim Job `pr-gate`. `CiWorkflowTests` prüft sie gegen alle Kombinationen aus PR-Autor und Auslöser. Außerdem prüft der Test, dass die übrigen Pflicht-Jobs ohne Bedingung laufen und dass der Branch-Schutz alle vier Jobs verlangt.
  - Ein übersprungener Job zählt für den Branch-Schutz als bestanden. Alle anderen Checks bleiben Pflicht (`scripts/github-einrichten.sh`).
  - `/pr-review` hat einen eigenen Prüfpunkt für Änderungen an `.github/workflows/` und am Branch-Schutz. Eine Ausweitung der Ausnahme braucht eine neue ADR.
