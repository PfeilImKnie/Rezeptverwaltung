# 0007 – Jeder PR enthält unabhängigen Test, Code-Review und Dokumentation

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** Arbeitsweise

## Kontext

Es gibt keinen zweiten menschlichen Entwickler für Reviews, und ein großer Teil des Codes entsteht mit
KI-Unterstützung. Wer Code schreibt, testet und reviewt typischerweise mit derselben Annahme, die zum
Fehler geführt hat (Bestätigungsfehler).

## Entscheidung

Jeder PR durchläuft vor dem Erstellen den Skill **`/pr-erstellen`**. Er führt nacheinander aus:

1. **Lokale Qualitätsprüfung:** Format, Build, alle Tests.
2. **Unabhängiger Test (`/pr-test`):** Ein separater Agent (`tester`) **ohne den Gesprächsverlauf der Implementierung** leitet Testfälle aus der Anforderung ab, ergänzt fehlende Tests (nur unter `tests/`) und führt Mutation Testing aus. Gefundene Fehler meldet er, er behebt sie nicht.
3. **Dokumentation (`/adr`):** ADR für architekturrelevante Entscheidungen, Glossar, README. Sonst gibt es eine begründete Aussage, warum keine ADR nötig ist.
4. **Unabhängiges Code-Review (`/pr-review`):** Ein separater, rein lesender Agent (`reviewer`) prüft gegen Definition of Done und ADRs. Ergebnis: *Freigegeben* oder *Änderungen nötig*. Blocker und Major-Befunde werden behoben, danach folgt ein erneutes Review.
5. **PR** mit den Berichten aus 2–4 in der Beschreibung.

Die Reihenfolge ist bewusst gewählt: Das Review läuft zuletzt und sieht auch die vom Tester ergänzten
Tests und die Dokumentation.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Review und Test durch denselben Agenten, der implementiert | Schnell | Bestätigungsfehler, kaum unabhängig |
| Nur CI-Checks | Objektiv | Prüft keine Fachlichkeit, kein Design, keine Doku |
| **Getrennte Agenten mit eigenem Kontext + CI-Gate** | Unabhängige Perspektive, nachvollziehbar im PR | Längere Laufzeit pro PR |

## Konsequenzen

- **Positiv:** Jeder PR dokumentiert, wie er getestet und reviewt wurde.
- **Negativ:** Ein PR dauert länger. Agenten-Reviews ersetzen kein fachliches Urteil der Anwenderin, deshalb wird Fachliches mit ihr abgestimmt.
- **Absicherung:**
  - Der CI-Job `pr-gate` prüft, dass die PR-Beschreibung die Abschnitte „Unabhängiger Test“, „Code-Review“ (mit Ergebnis *Freigegeben*) und „Dokumentation“ enthält und keine offenen DoD-Checkboxen hat.
  - Der Branch-Schutz auf `main` verlangt einen PR und grüne Checks, auch für Admins. Er erlaubt nur Squash-Merges und lineare Historie. Die Einrichtung ist als Code in `scripts/github-einrichten.sh` hinterlegt und muss nach jeder Umbenennung eines CI-Jobs erneut laufen.
