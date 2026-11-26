# 0016 – Weboberfläche als Blazor Web App nach dem BFF-Muster

- **Status:** Akzeptiert
- **Datum:** 2026-11-24
- **Betrifft:** neues Frontend (`src/Web`), API-Vertrag (Liste, Berechtigungen), lokale Entwicklung, Tests, CI. Ergänzt [ADR 0004](0004-identity-provider-fuer-oauth2-und-alexa.md), [0005](0005-hosting-auf-hetzner-mit-docker-compose.md) und [0006](0006-teststrategie.md).

## Kontext

- Die Konditormeisterin braucht eine Oberfläche, um die App im Alltag zu benutzen. Vorgabe vom 2026-11-24: **erst die Weboberfläche mit Login, dann das Hosting**. Die erste Version soll alles können, was die API kann.
- Benutzt wird die Oberfläche vor allem auf einem **Tablet in der Backstube**: mit Fingern, oft mit wackligem WLAN, Zahlen mit Komma („12,5 g“).
- Die API ist reiner Resource Server mit Bearer-Tokens von Zitadel (ADR 0004, 0015). Alexa kommt später als weiterer Client dazu.
- Tokens im Browser (Local/Session Storage) kann jedes eingeschleuste Skript (XSS) auslesen. Für Browser-Apps empfiehlt die IETF deshalb das Muster *Backend for Frontend* (BFF): Tokens bleiben auf dem Server, der Browser hält nur ein Cookie.
- Das Team arbeitet mit .NET. Eine zweite Sprache und ein zweiter Build kosten dauerhaft Aufwand.
- Die Oberfläche soll Aktionen ausblenden, die die Rolle nicht darf. Die Rechte legt aber die API fest (ADR 0015).

## Entscheidung

1. **Blazor Web App in zwei Projekten:**
   - `Rezeptverwaltung.Web` ist der Server (BFF).
   - `Rezeptverwaltung.Web.Client` sind die Seiten, die als WebAssembly im Browser laufen, **ohne Vorab-Rendern** auf dem Server.
   - Die Weboberfläche ist ein **eigener Prozess bzw. Container**. Sie spricht mit der API nur über den HTTP-Vertrag, wie später Alexa. Sie kennt weder Module noch SharedKernel (ArchUnit-Regel).
2. **Login nach dem BFF-Muster:**
   - Der Server ist ein *vertraulicher* OIDC-Client: Authorization Code mit PKCE und Client-Secret, Scopes `openid profile offline_access` plus konfigurierbare Zusatz-Scopes (Zitadel: Projekt-Zielgruppe und Rollen).
   - Die Tokens liegen nur im **verschlüsselten Sitzungs-Cookie**: `HttpOnly`, `SameSite=Lax`, `Secure` über HTTPS. Die Sitzung gilt 14 Tage gleitend und übersteht einen Neustart des Tablets.
   - Der Access Token wird **2 Minuten vor Ablauf** mit dem Refresh Token erneuert, ein neuer ID-Token wird dabei wie beim Login geprüft. Scheitert die Erneuerung (abgelehnt oder Identity Provider nicht erreichbar), gilt der alte Token bis zu seinem Ablauf weiter, und der nächste Aufruf versucht es neu. Danach endet die Sitzung.
   - Gleichzeitige Anfragen mit demselben Refresh Token teilen sich **eine** Erneuerung. Zitadel rotiert Refresh Tokens, eine zweite Erneuerung würde abgelehnt.
   - Abmelden ist ein Formular-POST mit Antiforgery-Token und meldet auch beim Identity Provider ab.
3. **Weiterleitung an die API:**
   - `/api/**` geht per YARP an die API, mit dem Access Token der Sitzung als Bearer.
   - Cookies und der CSRF-Header werden dabei entfernt, ein vom Browser mitgeschickter `Authorization`-Header wird ersetzt.
4. **Zugriffsschutz im Server:**
   - Die Fallback-Policy verlangt für alles eine Anmeldung. Seiten leiten ohne Sitzung zum Login, `/api` und `/bff` antworten mit **401** statt einer Weiterleitung.
   - Ohne Anmeldung erreichbar sind nur `/health`, statische Dateien, der Login selbst und `/abmelden` (prüft das Antiforgery-Token, ohne Sitzung geht es nach `/`).
   - Sitzungs- und Antiforgery-Cookie sind außerhalb der Entwicklung immer `Secure`, auch wenn hinter dem TLS-Proxy die Forwarded Headers fehlen sollten.
   - **CSRF-Schutz:** `/api` und `/bff` verlangen den Header `X-CSRF: 1`, sonst 403. Eine fremde Seite kann ihn nur nach einer CORS-Vorabprüfung setzen, und die wird nie erlaubt.
   - Rücksprungadressen nach dem Login sind nur lokale Pfade.
5. **API-Erweiterungen (Vertrag zuerst, abwärtskompatibel):**
   - `GET /api/v1/rezepte?suche=` liefert die Rezeptliste. Groß-/Kleinschreibung und Akzente zählen bei der Suche nicht. Sortiert wird alphabetisch, höchstens 100 Zeichen Suchtext (Value Object `Rezeptsuche`).
   - `GET /api/v1/rezepte/berechtigungen` sagt, was die Person darf. Die Oberfläche blendet danach aus, maßgeblich bleibt die Prüfung jeder Operation.
6. **Bedienung:**
   - Ausgelegt aufs Tablet: Schaltflächen mindestens 48 px, hell und dunkel.
   - **Zahlen auf Deutsch**, unabhängig von der Spracheinstellung des Browsers: Komma oder Punkt als Dezimaltrenner, aber keine Tausenderpunkte bei der Eingabe. „1.000“ wird abgewiesen, statt still als 1 g zu gelten.
   - Änderungen gelten bei jeder Eingabe, nicht erst beim Verlassen des Felds. Sonst ginge der erste Tipp auf „Speichern“ verloren.
   - Ein Tausenderpunkt wird mit eigener Meldung abgewiesen („Bitte ohne Tausenderpunkt eingeben, z. B. 10000“).
   - Bricht die Verbindung ab (WLAN) oder läuft eine Anfrage in eine Zeitüberschreitung, zeigt die Seite eine Meldung. Eingaben bleiben stehen und lassen sich erneut speichern.
   - Ein Faktor größer 0 wird nie als „0“ angezeigt.
   - Die App ist als PWA installierbar (Manifest). Offline-Betrieb gibt es vorerst nicht.
7. **Lokal** startet der AppHost die Weboberfläche auf `http://localhost:5090`. Der Test-Login zeigt dort eine Personenauswahl (Inhaber/in, Konditor/in, Mitarbeiter/in) statt eines Passworts.
8. **Tests:**
   - **bUnit** testet Seiten und Komponenten gegen eine API-Attrappe.
   - **BFF-Integrationstests** laufen mit `WebApplicationFactory`, dem echten Login-Server und der echten API samt PostgreSQL: Login, Token-Weiterleitung, CSRF, Abmelden, Token-Erneuerung.
   - **Playwright mit Chromium** prüft die Kernabläufe im echten Browser auf Kestrel, im CI-Job `build-test`.
   - Login-Server und API-Fabrik liegen gemeinsam in `tests/Rezeptverwaltung.Testumgebung`.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Blazor WebAssembly standalone, Tokens im Browser | Einfachster Aufbau, rein statisch | Tokens per XSS auslesbar; Refresh Tokens im Browser |
| Blazor Server (interaktiv über SignalR) | Kein WebAssembly-Download, kein Proxy | Dauerverbindung bricht bei wackligem WLAN ab; Token-Erneuerung in der Verbindung umständlich |
| React/TypeScript mit BFF | Größtes UI-Ökosystem | Zweite Sprache und zweiter Build, Modelle doppelt |
| Nur serverseitige Seiten (Razor Pages, Formulare) | Robust, wenig JavaScript | Zeilen hinzufügen, verschieben und umrechnen nur mit vielen Seitenwechseln |
| BFF im API-Host | Ein Container weniger | Vermischt Cookie-Anmeldung mit dem reinen Resource Server, den auch Alexa nutzt |
| **Blazor Web App mit BFF** | Keine Tokens im Browser, alles in C#, gut testbar | Ein Container mehr; Proxy und Token-Erneuerung selbst betreiben |

## Konsequenzen

- **Positiv:**
  - Kein Token gelangt in den Browser. Weitere Clients (Alexa) nutzen dieselbe API ohne Änderung.
  - Oberfläche und Rechte bleiben konsistent, weil die API sie vorgibt.
- **Negativ / Kosten:**
  - Ein zusätzlicher Container mit eigener Konfiguration: Client-Secret, Schlüsselverzeichnis für Data Protection.
  - Beim ersten Aufruf lädt der Browser die WebAssembly-Laufzeit, danach kommt sie aus dem Cache.
  - Die CI lädt Chromium (zwischengespeichert).
- **Absicherung:**
  - `BffTests`: Weiterleitung zum Login, 401 statt Weiterleitung, kein Token in Antworten oder Seitenzustand, Cookie-Attribute, Bearer statt Cookie an die API, CSRF, Abmelden, offene Weiterleitung, Token-Erneuerung.
  - `ArchitekturTests`: Die Weboberfläche kennt nur HTTP, der Browser-Teil kennt den Server-Teil nicht.
  - Seitentests (bUnit) und `BrowserTests` (Playwright) für Rollen und Kernabläufe.
  - `RezeptlisteApiTests` und `AnmeldungApiTests` prüfen die neuen Operationen automatisch auf 401.

## Offene Punkte

- **Hosting (eigener PR):**
  - Caddy terminiert TLS, die Weboberfläche braucht deshalb *Forwarded Headers*, damit Redirect-URI und `Secure`-Cookies stimmen.
  - `DataProtection:Verzeichnis` liegt in einem Volume.
  - In Zitadel wird eine Web-Anwendung angelegt (vertraulich, JWT-Access-Tokens, Projekt-Zielgruppe).
- **Geteiltes Tablet:** Arbeiten mehrere Personen am selben Gerät, handeln alle unter der angemeldeten Person. Ob es einen Gerätemodus oder kürzere Sitzungen braucht, klärt die Fachseite.
- Offline-Nutzung (Service Worker) erst bei Bedarf.
