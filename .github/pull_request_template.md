<!-- Wird von /pr-erstellen ausgefüllt. Der CI-Job "pr-gate" prüft die Abschnitte und offene Checkboxen. -->

## Anforderung

<!-- Akzeptanzkriterien, überprüfbar formuliert -->

## Änderungen

<!-- Was wurde geändert und warum, kurz -->

## Unabhängiger Test

<!-- Bericht aus /pr-test -->

## Code-Review

<!-- Bericht aus /pr-review (letzte Runde), muss **Ergebnis: Freigegeben** enthalten -->

## Dokumentation

<!-- Ergebnis aus /adr -->

## Definition of Done

<!-- Jede Zeile abhaken oder durch "entfällt: <Grund>" ersetzen. Vollständige Liste: docs/definition-of-done.md -->

- [ ] Fachlichkeit: Anforderung erfüllt, Glossar aktuell, Invarianten im Domain-Modell
- [ ] Code: Build ohne Warnungen, Format sauber, Architekturtests grün
- [ ] Tests: Domain, Application und API, property-based für Rechenlogik, Mutation Score ≥ 80 %
- [ ] API: Vertrag zuerst angepasst, Contract-Test grün, abwärtskompatibel
- [ ] Sicherheit: Eingaben begrenzt, keine Secrets und keine sensiblen Daten in Logs, keine verwundbaren Pakete
- [ ] Dokumentation: ADR bzw. Begründung, README und CLAUDE.md aktuell
- [ ] Unabhängiger Test durchgeführt
- [ ] Code-Review freigegeben, keine offenen Blocker oder Major-Befunde
