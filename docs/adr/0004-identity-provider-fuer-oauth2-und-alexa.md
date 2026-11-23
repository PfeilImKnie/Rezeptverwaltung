# 0004 – Externer Identity Provider (OIDC) für Login und Alexa-Account-Linking

- **Status:** Akzeptiert (Umsetzung ausstehend)
- **Datum:** 2026-11-23
- **Betrifft:** Authentifizierung, Alexa-Adapter, Infrastruktur

## Kontext

- Die App soll mit Amazon Alexa funktionieren. Alexa ruft einen öffentlich erreichbaren HTTPS-Endpoint auf. **Ein reiner VPN-Zugang (WireGuard) scheidet deshalb aus.**
- Alexa verbindet Konten per **Account Linking**: OAuth 2.0 Authorization Code Grant mit Refresh Tokens.
- Wenige Nutzer, aber gute Sicherheit ist gewünscht. Eigene Passwort- oder Token-Logik ist ein Sicherheitsrisiko.

## Entscheidung

1. Wir betreiben einen **externen Identity Provider**. Bevorzugt ist **Zitadel** (selbst gehostet, OIDC-zertifiziert, Passkeys/MFA, nutzt PostgreSQL). Die Alternative ist Keycloak.
2. Die API ist ein reiner **Resource Server**: Sie validiert JWT Access Tokens (`Microsoft.AspNetCore.Authentication.JwtBearer`) und kennt keine Passwörter.
3. Alle Clients (PWA, Alexa) holen Tokens beim IdP. Alexa nutzt Account Linking. Der Alexa-Adapter reicht das Token des Nutzers an die API weiter.
4. Für Nutzer werden **Passkeys oder MFA** erzwungen.
5. Bis zur Umsetzung ist die API **nicht öffentlich erreichbar** (nur lokal, nur Tests).

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Nur WireGuard/Tailscale | Minimale Angriffsfläche | Alexa kann nicht zugreifen |
| OpenIddict in der App | .NET-nativ, ein Deployable weniger | Wir verantworten einen Auth-Server selbst |
| Keycloak | Industriestandard | Spürbar mehr RAM, komplexer |
| Gehosteter IdP (Auth0, Entra External ID) | Kein Betrieb | Abhängigkeit, Datenschutzfragen, Kosten bei Wachstum |
| **Zitadel selbst gehostet** | Schlank, modern, Passkeys | Eine Komponente mehr im Betrieb |

## Konsequenzen

- **Positiv:** Standardkonforme Authentifizierung, und jedes künftige Frontend funktioniert ohne API-Änderung.
- **Negativ:** Eine weitere Komponente mit Backups und Updates.
- **Absicherung:** Ab Umsetzung verlangt die Definition of Done Authentifizierung für jeden Endpoint. Ein Architektur- oder API-Test prüft, dass kein Endpoint außer `/health` und `/openapi` anonym erreichbar ist.

## Offene Punkte

- Endgültige Wahl Zitadel oder Keycloak beim Umsetzungs-PR. Falls sich die Wahl ändert, gibt es eine eigene ADR.
- Rollenmodell (z. B. Inhaberin, Mitarbeiter), sobald es mehr als eine Nutzerin gibt.
