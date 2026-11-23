# 0015 – Betriebe (Mandanten), Rollen und Anmeldung in der API

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** alle Module, SharedKernel, API-Host, Persistenz, Tests, CI. Setzt [ADR 0004](0004-identity-provider-fuer-oauth2-und-alexa.md) auf API-Seite um.

## Kontext

Vorgaben (2026-11-23):

- **Mehrere Betriebe:** Später sollen auch andere Konditoreien die App nutzen, streng getrennt.
- **Mehrere Personen je Betrieb** mit Rollen:
  - Inhaber/in darf alles, auch freigeben.
  - Konditor/in legt an und bearbeitet oder verwirft Entwürfe, gibt aber nicht frei.
  - Mitarbeiter/in liest und rechnet um.
- **Ziel „bald produktiv nutzbar“:** Bevor die API öffentlich wird (ADR 0005), braucht es Anmeldung, Trennung der Betriebe und Grenzen für Eingaben (Issue #6).
- Identity Provider ist Zitadel (ADR 0004). Zitadel kennt *Organisationen* und Projekt-Rollen je Organisation. Rollen stehen im Token als `urn:zitadel:iam:org:project:roles` = `{ rolle: { organisationId: domain } }`.
- Eine Mandantentrennung nachträglich einzuziehen ist teuer, weil jede Tabelle, jede Abfrage und jeder Test betroffen ist.

## Entscheidung

1. **Betrieb = Organisation im IdP.**
   - Jedes Aggregat gehört genau einem `BetriebId` (SharedKernel). Er wird beim Anlegen gesetzt und ändert sich nie.
   - Der Betrieb ergibt sich aus den **Rollen-Zuweisungen** im Token, nicht aus der Heimat-Organisation der Person. Zugriff gibt es nur dort, wo eine Rolle vergeben ist.
   - Rollen in mehreren Betrieben → 403 `betrieb.nicht_eindeutig`. Ein Betriebswechsel ist eine spätere Erweiterung.
2. **Rollen** `mitarbeiter`, `konditor`, `inhaber` (SharedKernel `Rolle`). **Welche Rolle was darf, legt jedes Modul fest**, für Rezeptur in `Rechte`. Unbekannte Rollen geben kein Recht.
3. **Durchsetzung in drei Schichten:**
   - **Anmeldung (Host):**
     - JwtBearer prüft Signatur (JWKS), Aussteller, Zielgruppe und Gültigkeit.
     - Die **Fallback-Policy verlangt für jeden Endpunkt eine Anmeldung**. Offen sind nur `/health` und `/openapi`.
     - `RequireHttpsMetadata=false` ist in Produktion verboten (Startabbruch).
   - **Recht (Use Cases):** Jeder Use Case prüft zuerst das Recht (403 `recht.fehlt`) und behandelt Rezepte anderer Betriebe als nicht vorhanden. Das ist die zweite Verteidigungslinie und ohne Datenbank testbar.
   - **Daten (Persistenz):**
     - Ein globaler **Mandantenfilter** (`HasQueryFilter`) beschränkt jede Abfrage auf den Betrieb der angemeldeten Person. Ohne Betrieb liefert die Datenbank nichts.
     - Das Repository legt nur Rezepte im eigenen Betrieb an.
4. **Fremde Daten verhalten sich wie nicht vorhanden (404).** Die Antwort ist identisch zu „gibt es nicht“, damit nicht einmal die Existenz erkennbar ist.
5. **Prüfreihenfolge:** 413 → 401 → 403 → 404 → 428/412 → 422 → 409. Die Größenprüfung kommt bewusst vor der Anmeldung, damit kein übergroßer Body angenommen wird, auch nicht von Unangemeldeten.
6. **Grenzen (Issue #6):**
   - Höchstens **100 Zutatenzeilen** je Fassung (422 `fassung.zu_viele_zutaten`, beim Hinzufügen 409).
   - Anfragen höchstens **1 MB**: Prüfung der Längenangabe → 413, Kestrel-Grenze auch für Anfragen ohne Längenangabe.
   - Rate Limiting folgt mit dem Hosting (Caddy/ASP.NET, ADR 0005).
7. **Bestandsdaten:** Die Migration ordnet vorhandene Rezepte dem Betrieb `altbestand` zu. Eine Zuordnung zu einem echten Betrieb erfolgt per SQL (README). `altbestand` ist **reserviert**: Kein Token gibt darauf Zugriff. Hat eine bestehende Fassung mehr als 100 Zeilen, ist ein neuer Entwurf daraus ein Zustandskonflikt (409 `fassung.zu_viele_zutaten`).
8. **Tests und lokale Entwicklung ohne Sonderwege im Produktionscode:**
   - Ein echter OIDC-Aussteller (`mock-oauth2-server` 6.1.4) stellt Tokens im Zitadel-Format aus: in Tests per Testcontainers, in der CI als Service, lokal im AppHost.
   - Die API prüft diese Tokens genauso wie später die von Zitadel.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Eine Datenbank oder ein Schema je Betrieb | Stärkste Trennung | Migrationen × Betriebe, Betriebsaufwand; für wenige Betriebe überdimensioniert |
| PostgreSQL Row-Level Security zusätzlich | Trennung auch bei Fehlern im Anwendungscode | Sitzungsvariable je Verbindung, komplexer mit EF-Pooling; als spätere Verstärkung möglich |
| Nur Prüfung in den Endpunkten | Einfach | Ein vergessener Filter legt fremde Daten offen; nicht ohne HTTP testbar |
| Betrieb aus der Heimat-Organisation des Tokens | Immer genau ein Wert | Zugriff ohne Rollen-Zuweisung; widerspricht „Zugriff nur mit Rolle“ |
| Test-Authentifizierung per Fake-Schema | Schnell | Prüft nicht die echte Token-Validierung; zusätzlicher Codepfad |
| 403 statt 404 für fremde Rezepte | Ehrlicher Status | Verrät die Existenz fremder Rezepte |

## Konsequenzen

- **Positiv:**
  - Weitere Betriebe lassen sich ohne Umbau aufnehmen.
  - Jede Rolle sieht genau das, was sie darf.
  - Eine vergessene Prüfung an einer Stelle fängt eine andere Schicht ab.
- **Negativ:**
  - Alle Clients brauchen Tokens.
  - Personen mit Rollen in mehreren Betrieben können sich noch nicht anmelden.
  - Bestandsdaten müssen einmal einem Betrieb zugeordnet werden.
- **Absicherung:**
  - `AnmeldungApiTests` prüft **jede Operation aus dem Vertrag** ohne Token sowie mit gefälschten, unsignierten, fremden oder falsch adressierten Tokens auf 401.
  - `RollenUndBetriebeApiTests` deckt die Rollenmatrix ab, prüft, dass ein fremder Betrieb bei allen 9 Operationen 404 bekommt und dass die Antwort identisch zu „nie vorhanden“ ist.
  - `RechteUseCaseTests` prüft die Matrix und die Trennung ohne Datenbank.
  - `MandantenfilterTests` prüft den Filter an der Datenbank, auch für mehrere Kontexte nacheinander.
  - `TokenBenutzerTests` prüft die Auswertung des Claims.
  - Schemathesis prüft mit `ignored_auth`, dass Anfragen ohne Token abgewiesen werden.
