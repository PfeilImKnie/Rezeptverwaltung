# Definition of Done

Ein Pull Request ist **fertig**, wenn alle zutreffenden Punkte erfüllt sind. Punkte, die nicht zutreffen,
werden in der PR-Beschreibung mit Begründung als „entfällt“ markiert, nicht einfach weggelassen.

Testen, Code-Review und Dokumentation sind **Pflichtbestandteil jedes PRs** (ADR 0007).
Der Ablauf ist im Skill `/pr-erstellen` festgelegt.

## 1. Fachlichkeit

- [ ] Die Anforderung (Akzeptanzkriterien) ist in der PR-Beschreibung formuliert und vollständig umgesetzt.
- [ ] Fachbegriffe entsprechen der Ubiquitous Language; neue Begriffe stehen im [Glossar](glossar.md).
- [ ] Fachliche Regeln (Invarianten) liegen im Domain-Modell, nicht in Controllern, Handlern oder im Frontend.

## 2. Code

- [ ] `dotnet build` ohne Warnungen (Warnungen sind Fehler).
- [ ] `dotnet format --verify-no-changes` ist sauber.
- [ ] Architekturtests grün: Domain ohne Framework-Abhängigkeiten, keine Zugriffe zwischen Modulen (ADR 0002).
- [ ] Keine auskommentierten Code-Reste, kein `TODO` ohne verlinktes Issue.
- [ ] Keine neuen Abhängigkeiten ohne Begründung in der PR-Beschreibung (bei Tragweite: ADR).

## 3. Tests (ADR 0006)

- [ ] Jede neue oder geänderte Fachregel hat Domain-Tests, mit Grenzwerten und Fehlerfällen.
- [ ] Rechenlogik (Skalierung, Mengen, Kosten …) ist zusätzlich **property-based** getestet.
- [ ] Jeder Use Case hat Application-Tests (mit Fakes für Ports).
- [ ] Jeder Endpoint hat API-Tests für Erfolg **und** jeden im Vertrag genannten Fehlerstatus.
- [ ] Persistenz-Änderungen haben Integrationstests gegen echtes PostgreSQL (Testcontainers), sobald vorhanden.
- [ ] **Mutation Score der Domain ≥ 80 %** (Stryker, bricht die CI darunter ab).
- [ ] Alle Tests sind in der CI grün; kein Test wird übersprungen oder auskommentiert.
- [ ] **Unabhängiger Test** durchgeführt (`/pr-test`), Bericht in der PR-Beschreibung.

## 4. API (ADR 0003)

- [ ] `api/openapi.yaml` wurde **vor** der Implementierung angepasst.
- [ ] Contract-Test (`OpenApiVertragTests`) und Schemathesis sind grün.
- [ ] Änderungen innerhalb von `v1` sind abwärtskompatibel; Breaking Changes nur mit neuer Version und ADR.
- [ ] Fehler werden als Problem Details geliefert: 400 = Format, 404 = nicht gefunden, 422 = Fachregel mit `code`.

## 5. Sicherheit

- [ ] Neue Endpoints sind authentifiziert und autorisiert (sobald ADR 0004 umgesetzt ist).
- [ ] Alle Eingaben werden begrenzt (Länge, Wertebereich, Genauigkeit), im Vertrag **und** im Domain-Modell.
- [ ] Keine Secrets im Repository; Konfiguration über Umgebungsvariablen oder Secret Store.
- [ ] Keine personenbezogenen Daten und keine Tokens in Logs.
- [ ] Keine bekannten Schwachstellen in Abhängigkeiten (NuGet-Audit bricht den Build).

## 6. Dokumentation (ADR 0001)

- [ ] Architekturrelevante Entscheidungen sind als ADR festgehalten (`/adr`), oder die PR-Beschreibung begründet, warum keine nötig ist.
- [ ] README / CLAUDE.md sind aktuell, wenn sich Setup, Befehle oder Konventionen geändert haben.
- [ ] Glossar ist aktuell.

## 7. Review (ADR 0007)

- [ ] Unabhängiges Code-Review durchgeführt (`/pr-review`), Ergebnis **Freigegeben**.
- [ ] Keine offenen Befunde der Schwere *Blocker* oder *Major*; *Minor* sind behoben oder als Issue erfasst.
