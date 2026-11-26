# 0017 – Neue Betriebe legt der Betreiber an, Personen lädt die Inhaberin bzw. der Inhaber ein

- **Status:** Akzeptiert (Umsetzung ausstehend)
- **Datum:** 2026-11-24
- **Betrifft:** Identity Provider (Zitadel), Betrieb, künftiges Modul für Personenverwaltung, Weboberfläche. Ergänzt [ADR 0015](0015-betriebe-rollen-und-anmeldung.md).

## Kontext

- Seit ADR 0015 ergibt sich der Betrieb aus den Rollen-Zuweisungen im Token: Betrieb = Organisation in Zitadel, Rolle = Projekt-Rolle in dieser Organisation. Offen war, **wie ein neuer Betrieb und eine neue Person ins System kommen**.
- Vorgaben vom 2026-11-24:
  - Neue Betriebe legt **nur der Betreiber** an, es gibt keine offene Registrierung.
  - Weitere Personen lädt die **Inhaberin bzw. der Inhaber in der App** ein: per E-Mail mit Rolle, Rollen ändern, Personen entfernen.
- Wenige Betriebe, wenige Personen, ein Betreiber. Eine offene Registrierung zöge Missbrauch und Pflegeaufwand nach sich.
- Die Zitadel-Konsole kann das zwar, ist aber technisch und nicht auf die App zugeschnitten.
- Zitadel bietet dafür eine Management-API: Organisationen, Projekt-Freigaben (*Project Grants*), Benutzer, Rollen-Zuweisungen (*User Grants*) und Einladungs-Mails.

## Entscheidung

1. **Neuer Betrieb nur durch den Betreiber** über ein Skript (`scripts/betrieb-anlegen.sh`, kommt mit dem Hosting-PR). Es spricht über einen Service-Account mit der Zitadel-Management-API und legt in einem Lauf an:
   - die Organisation (= Betrieb),
   - die Freigabe des Projekts mit den Rollen `inhaber`, `konditor`, `mitarbeiter`,
   - die erste Person mit der Rolle `inhaber`. Zitadel schickt ihr eine Einladung, bei der ersten Anmeldung richtet sie Passkey oder MFA ein (ADR 0004).
2. **Personen lädt die Inhaberin bzw. der Inhaber in der Weboberfläche ein** (eigener PR nach dem Hosting):
   - Einladen per E-Mail mit Rolle, Rolle ändern, Person aus dem Betrieb entfernen (Rollen-Zuweisung löschen). Das darf nur `inhaber`.
   - Die API bekommt dafür ein eigenes Modul. Es spricht über einen Port mit dem Identity Provider, der Adapter nutzt die Zitadel-Management-API.
   - Der Service-Account darf nur Benutzer und Rollen-Zuweisungen verwalten. Die API prüft, dass nur der eigene Betrieb betroffen ist.
   - Der Betrieb behält immer mindestens eine Person mit der Rolle `inhaber`.
3. Es gibt **keine Selbstregistrierung**, weder für Betriebe noch für Personen.
4. Personen mit Rollen in mehreren Betrieben werden weiterhin nicht unterstützt (ADR 0015).

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Selbstregistrierung neuer Betriebe | Kein Aufwand für den Betreiber | Missbrauch, Spam, Pflege fremder Betriebe; braucht Freischaltung oder Bezahlmodell |
| Registrierung mit Freischaltung | Kontrolle bleibt beim Betreiber | Zusätzlicher Ablauf und Oberfläche für wenige Fälle |
| Personen über die Zitadel-Konsole | Kein eigener Code | Technische Oberfläche, Inhaberin braucht Admin-Rechte in Zitadel |
| Personen nur durch den Betreiber | Am wenigsten Code | Betreiber wird für jede Aushilfe gebraucht |
| **Betriebe per Skript, Personen in der App** | Kontrolle über Betriebe, Alltag ohne Betreiber | Eigenes Modul und Adapter zu Zitadel nötig |

## Konsequenzen

- **Positiv:**
  - Kein offener Registrierungsweg.
  - Der Betrieb verwaltet sein Team selbst, in der gewohnten Oberfläche.
- **Negativ / Kosten:**
  - Ein Service-Account mit Verwaltungsrechten in Zitadel muss geschützt werden (Secret, Rotation).
  - Die Personenverwaltung hängt an der Management-API von Zitadel. Ein Wechsel des Identity Providers betrifft nur den Adapter.
- **Absicherung:**
  - Das Skript ist idempotent und im Runbook beschrieben.
  - Die Einladungsfunktion bekommt Tests für Rechte (nur `inhaber`), Betriebsgrenzen und „mindestens eine Inhaberin bzw. ein Inhaber“.
  - Der Zitadel-Adapter wird gegen einen echten Zitadel-Container getestet.

## Offene Punkte

- Ob Personen ganz gelöscht oder nur aus dem Betrieb entfernt werden. Vorschlag: nur entfernen, das Konto bleibt bei Zitadel.
- Gestaltung der Einladungs-Mail (Absender, Text) beim Einrichten von Zitadel.
