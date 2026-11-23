# 0001 – Architekturentscheidungen werden als ADR dokumentiert

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** gesamtes Projekt

## Kontext

Das Projekt wird über längere Zeit nebenberuflich entwickelt, teils mit KI-Unterstützung. Begründungen für
Entscheidungen gehen ohne schriftliche Form verloren. Später ist dann nicht mehr klar, ob eine Regel bewusst
gewählt wurde oder Zufall ist.

## Entscheidung

Wir halten jede architekturrelevante Entscheidung als **Architecture Decision Record** in `docs/adr/` fest:

- Format: [Vorlage](0000-vorlage.md), angelehnt an MADR, auf Deutsch.
- Dateiname `NNNN-titel-in-kebab-case.md`, fortlaufend nummeriert, Index in [README](README.md).
- ADRs werden **nicht inhaltlich umgeschrieben**, wenn sich eine Entscheidung ändert. Eine neue ADR löst die alte ab, und die alte bekommt den Status „Abgelöst durch …“.
- Die ADR entsteht im **selben PR** wie die Umsetzung (Skill `/adr`).

Architekturrelevant ist eine Entscheidung, wenn sie mindestens eins davon betrifft:
Modulgrenzen oder Schichten, API-Vertrag oder Versionierung, Persistenz oder Datenmodell, Sicherheit oder
Authentifizierung, Infrastruktur oder Betrieb, neue Bibliothek oder Framework mit Tragweite, Teststrategie,
Arbeitsweise.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Wiki / Confluence | Komfortabel | Getrennt vom Code, veraltet unbemerkt, nicht im Review |
| Nur Code-Kommentare | Nah am Code | Keine Alternativen und kein Kontext, über Dateien verstreut |
| **ADRs im Repo** | Versioniert, im PR reviewt, für Menschen und KI lesbar | Disziplin nötig |

## Konsequenzen

- **Positiv:** Entscheidungen sind nachvollziehbar und werden im Code-Review mitgeprüft.
- **Negativ:** Etwas Mehraufwand pro PR.
- **Absicherung:** Die Definition of Done verlangt eine ADR oder eine Begründung, warum keine nötig ist. Das Code-Review prüft das, und die CI prüft den Abschnitt „Dokumentation“ in der PR-Beschreibung.
