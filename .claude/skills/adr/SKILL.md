---
name: adr
description: Dokumentiert Architekturentscheidungen als ADR in docs/adr/ (neu anlegen, ablösen, Index pflegen) und hält Glossar, README und CLAUDE.md aktuell. Verwenden, wenn eine Entscheidung Modulgrenzen, API-Vertrag, Persistenz, Sicherheit, Infrastruktur, neue Bibliotheken, Teststrategie oder Arbeitsweise betrifft, und als Dokumentationsschritt in /pr-erstellen. Liefert den Abschnitt "Dokumentation" für die PR-Beschreibung.
argument-hint: "[Entscheidung in einem Satz | --pruefen]"
---

# Dokumentation per ADR

Argumente: `$ARGUMENTS`. Entweder eine konkrete Entscheidung, oder `--pruefen` (bzw. leer): Dann prüfst du die
aktuellen Änderungen (`git diff main --stat`, `git status --porcelain`), ob sie dokumentationspflichtig sind.

## 1. Ist eine ADR nötig?

Lies `docs/adr/0001-architekturentscheidungen-als-adr.md`. Eine ADR ist nötig, wenn die Änderung mindestens
eins davon betrifft:

- Modulgrenzen, Schichten, neuer Bounded Context
- API-Vertrag: neue Ressource, Breaking Change, Versionierung, Fehlerformat
- Persistenz, Datenmodell, Migrationen mit Tragweite
- Sicherheit, Authentifizierung, Autorisierung
- Infrastruktur, Deployment, Betrieb, Backup
- neue Bibliothek oder Framework mit Tragweite (nicht: Patch-Update)
- Teststrategie, Arbeitsweise, Abweichung von einer bestehenden ADR

**Keine** ADR brauchen: neue Fachregel innerhalb eines bestehenden Aggregats, neuer Endpoint nach bestehendem
Muster, Bugfix, Refactoring ohne Strukturänderung. Diese gehören ins Glossar oder in die PR-Beschreibung.

Lies `docs/adr/README.md` und die betroffenen ADRs. **Widerspricht die Änderung einer bestehenden ADR?**
Dann ist eine neue ADR nötig, die sie ablöst. Alternativ wird die Änderung zurückgenommen. Ein stiller
Widerspruch ist nicht erlaubt.

## 2. ADR schreiben

1. Nächste Nummer = höchste vorhandene + 1 (vierstellig).
2. Datei `docs/adr/NNNN-<entscheidung-in-kebab-case>.md` aus `docs/adr/0000-vorlage.md`.
3. Deutsch, konkret, mit heutigem Datum. **Kontext** enthält nur Fakten und Kräfte. **Entscheidung** ist aktiv formuliert („Wir …“). Mindestens zwei **echte Alternativen** mit Pro und Contra. **Konsequenzen** enthalten immer eine **Absicherung**: Wie wird die Einhaltung geprüft (Test, Architekturregel, CI, Review)?
4. Status: `Akzeptiert`, wenn die Umsetzung im selben PR erfolgt, sonst `Vorgeschlagen` oder `Akzeptiert (Umsetzung ausstehend)`.
5. Ablösung: In der alten ADR **nur** den Status ändern („Abgelöst durch [NNNN](…)“). Der Inhalt bleibt unverändert.
6. `docs/adr/README.md`: Zeile in der Tabelle ergänzen bzw. Status anpassen.

## 3. Begleitende Dokumentation

- `docs/glossar.md`: neue oder geänderte Fachbegriffe, und erledigte „geplante Begriffe“ nach oben verschieben.
- `README.md`: Setup, Befehle, Projektstruktur.
- `CLAUDE.md`: Konventionen und Befehle, die künftige Sessions kennen müssen.
- XML-Doku und Kommentare im geänderten Code: stimmen sie noch?

## 4. Ergebnis (für die PR-Beschreibung)

```markdown
### Dokumentation

- ADR: [NNNN – Titel](docs/adr/NNNN-….md) (neu) | **keine ADR nötig:** <Begründung anhand der Kriterien>
- Abgelöst: … | –
- Glossar: <Begriffe> | unverändert
- README / CLAUDE.md: <was> | unverändert
```
