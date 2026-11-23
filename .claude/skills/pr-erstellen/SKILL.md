---
name: pr-erstellen
description: Einziger erlaubter Weg zu einem Pull Request in diesem Repository. Führt Qualitätsprüfung, unabhängigen Test (/pr-test), Dokumentation (/adr) und unabhängiges Code-Review (/pr-review) aus, behebt Befunde, committet, pusht und erstellt den PR mit allen Berichten. Verwenden, sobald eine Änderung fertig ist oder jemand "PR", "Pull Request", "mergen" oder "fertig machen" sagt.
argument-hint: "[Anforderung / Akzeptanzkriterien]"
---

# Pull Request erstellen (ADR 0007)

Argumente: `$ARGUMENTS`. Das ist die Anforderung. Fehlt sie, formuliere sie aus der Aufgabe dieser Session
als 1–5 überprüfbare Akzeptanzkriterien und **lass sie dir vom User bestätigen**, bevor du weitermachst.

## 0. Vorbedingungen

- `git branch --show-current` darf nicht `main` sein. Sonst Branch anlegen: `feature/<kurz>`, `fix/<kurz>` oder `docs/<kurz>`.
- Arbeitsbaum ansehen (`git status`). Gehören alle Änderungen zu dieser Anforderung? Fremdes nicht mit aufnehmen, sondern nachfragen.

## 1. Lokale Qualitätsprüfung

```bash
dotnet format Rezeptverwaltung.slnx
dotnet build Rezeptverwaltung.slnx
dotnet test --solution Rezeptverwaltung.slnx
```

Alles muss grün sein, bevor es weitergeht. Fehler behebst du selbst.

## 2. Unabhängiger Test

Skill `pr-test` mit **nur der Anforderung** als Argument aufrufen. Keine Hinweise zur Implementierung oder
dazu, „was schon getestet ist“: Die Unabhängigkeit entsteht gerade dadurch, dass der Tester das nicht weiß.

- **Fehler gefunden:** Ursache im Produktionscode beheben. Die roten Tests des Testers **nicht** ändern, außer sie sind nachweislich falsch; dann im PR begründen. Danach Schritt 1 wiederholen.
- Bericht für die PR-Beschreibung aufheben.

## 3. Dokumentation

Skill `adr` mit `--pruefen` aufrufen bzw. mit der konkreten Entscheidung. Ergebnisabschnitt aufheben.

## 4. Unabhängiges Code-Review

Skill `pr-review` mit `main <Anforderung>` aufrufen. Es läuft **nach** Test und Doku, damit es beides mit
prüft.

- **Änderungen nötig:** Blocker und Major beheben, Minor beheben oder als Issue notieren. Danach Schritt 1 und **erneut** `pr-review` ausführen. Nach drei Runden ohne Freigabe: stoppen und den User fragen.
- Den Bericht der **letzten** Runde verwenden. Die Behebungen früherer Runden kommen kurz unter „Review-Verlauf“.

## 5. Commit, Push, PR

Der Aufruf dieses Skills durch den User gilt als Auftrag zu committen, zu pushen und den PR zu erstellen.
Hast du den Skill selbstständig gestartet, fragst du vor dem Push nach.

1. Commits nach Conventional Commits (`feat(rezeptur): …`, `fix: …`, `docs(adr): …`, `test: …`). Keine Secrets, keine Build-Artefakte.
2. `git push -u origin <branch>`
3. PR-Beschreibung nach `.github/pull_request_template.md` füllen:
   - **Anforderung:** Akzeptanzkriterien
   - **Unabhängiger Test:** Bericht aus Schritt 2
   - **Code-Review:** Bericht aus Schritt 4. Muss `**Ergebnis: Freigegeben**` enthalten.
   - **Dokumentation:** Ergebnis aus Schritt 3
   - **Definition of Done:** Jede Checkbox abhaken (`- [x]`) oder als `entfällt: <Grund>` markieren. Es bleibt keine `- [ ]` offen, das prüft der CI-Job `pr-gate`.
4. `gh pr create --base main --title "<Conventional-Commit-Titel>" --body-file <datei>`.
   Fehlt `gh`, gibst du Titel und Beschreibung aus und nennst den Link zum Erstellen.

## 6. Abschluss

Dem User berichten: PR-Link, Ergebnis von Test und Review, gefundene und behobene Fehler, offene Punkte.
Die CI muss danach grün werden (`build-test`, `mutation`, `api-vertrag`, `pr-gate`). Schlägt sie fehl, gehört
die Behebung in denselben PR.
