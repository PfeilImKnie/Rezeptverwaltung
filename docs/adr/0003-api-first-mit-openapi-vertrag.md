# 0003 – API-first: `api/openapi.yaml` ist der Vertrag

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** REST-API, alle Frontends

## Kontext

Mehrere Frontends sind geplant: eine Tablet-PWA für die Backstube, ein Alexa-Skill, eventuell native Apps.
Sie sollen alle über dieselbe, stabile API arbeiten. ASP.NET Core erzeugt OpenAPI-Dokumente aus dem Code
(code-first). Dabei wird der Vertrag leicht zum Nebenprodukt, und Änderungen fallen niemandem auf.

## Entscheidung

1. **Vertrag zuerst:** `api/openapi.yaml` (OpenAPI 3.1) wird von Hand gepflegt und ist die Quelle der Wahrheit. Jede API-Änderung beginnt dort.
2. Die Implementierung muss dem Vertrag **exakt** entsprechen:
   - `OpenApiVertragTests` vergleicht Pfade, Methoden und Statuscodes zwischen `api/openapi.yaml` und dem von der laufenden API erzeugten Dokument `/openapi/v1.json`.
   - **Schemathesis** testet in der CI die laufende API gegen den Vertrag, mit generierten gültigen und ungültigen Anfragen und geprüften Antwortschemas.
3. **Einschränkungen im Vertrag und in der Domain stimmen überein** (Längen, Wertebereiche, Genauigkeit). Was der Vertrag als ungültig beschreibt, muss die API ablehnen, und umgekehrt.
4. **Versionierung über den Pfad** (`/api/v1/…`). Innerhalb einer Version gibt es nur abwärtskompatible Änderungen.
5. **Fehler** nach RFC 9457 (Problem Details): 400 für Format und Pflichtfelder, 404 für „nicht gefunden“, 422 für Fachregeln mit stabilem `code`.
6. **Clients** werden aus dem Vertrag generiert (Kiota), sobald es ein Frontend gibt.
7. API-Modelle (`*Request`, `*Response`) sind von Application-Modellen getrennt.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Code-first (nur generiertes Dokument) | Kein Doppelpflegeaufwand | Vertrag ändert sich unbemerkt |
| Server-Stubs aus der Spec generieren (NSwag) | Vertrag erzwungen | Generierter Code passt schlecht zu Minimal APIs und hexagonaler Struktur |
| **Handgeschriebene Spec + Contract-Tests** | Vertrag ist bewusst gestaltet, Abweichungen fallen in der CI auf | Spec und Code müssen beide gepflegt werden |

## Konsequenzen

- **Positiv:** Frontends können parallel gegen den Vertrag entwickelt werden. Breaking Changes fallen sofort auf.
- **Negativ:** Doppelpflege von Spec und Endpoint-Metadaten.
- **Absicherung:** Contract-Test (lokal und CI), Schemathesis (CI), Review-Checkliste.
