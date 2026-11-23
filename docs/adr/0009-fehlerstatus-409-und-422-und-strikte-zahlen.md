# 0009 – Fehlerstatus: 422 für ungültige Eingaben, 409 für Zustandskonflikte; Zahlen und Texte strikt nach Vertrag

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** REST-API, SharedKernel, alle Module. Präzisiert [ADR 0003](0003-api-first-mit-openapi-vertrag.md), Punkt 5.

## Kontext

ADR 0003 legte fest: „422 für Fachregeln“. Der erste Schemathesis-Lauf in der CI und der unabhängige Test
haben gezeigt, dass das nicht reicht:

- Ein Rezept ohne Zutaten zu skalieren lieferte 422. Die Anfrage selbst ist aber gültig, nur der **Zustand** des Rezepts lässt sie nicht zu. Für einen Client ist das ein grundlegend anderer Fall: Er muss nicht die Eingabe korrigieren, sondern erst das Rezept vervollständigen. Schemathesis meldet zu Recht, dass eine vertragsgemäße Anfrage abgelehnt wird.
- Bei einem leeren Rezept mit Zielgewicht 0 hing der Status davon ab, welche Regel zuerst geprüft wurde.
- `zielgewichtGramm=1,5` wurde als **15 g** gelesen, weil die Standardbindung von `decimal` Tausendertrennzeichen erlaubt. Für deutschsprachige Nutzer ist das ein stiller Fehler um den Faktor 10.
- `"gramm": "100"` (Zahl als String) wurde akzeptiert, obwohl der Vertrag `type: number` verlangt. Das liegt an den Web-Standards von System.Text.Json.
- Die Bezeichnung `"\u0085"` gilt im Vertrag (`\S`, ECMAScript) als sichtbares Zeichen, .NET-`Trim()` entfernt sie aber als Leerraum. Ergebnis: 422 für eine gültige Anfrage. Außerdem zählt JSON Schema `maxLength` Codepoints, die Domain zählte UTF-16-Einheiten (Issue #5).

## Entscheidung

1. Fachregeln haben eine **Regelart** (`Regelart` in `DomainRuleViolationException`):
   - `UngueltigeEingabe` (`DomainRuleViolationException.Wenn`) → **HTTP 422**
   - `Zustandskonflikt` (`DomainRuleViolationException.ZustandWenn`) → **HTTP 409 Conflict**
   Beide werden als Problem Details mit stabilem `code` geliefert.
2. **Erst die Eingabe, dann der Zustand:** Eine Operation prüft alle Eingaberegeln vor den Zustandsregeln. 409 setzt eine gültige Anfrage voraus.
3. **Zahlen strikt nach Vertrag:**
   - JSON: `JsonNumberHandling.Strict`. Zahlen als Strings ergeben 400.
   - Query-Parameter mit Gramm-Angaben werden über den Typ `Gramm` gebunden: optionales Vorzeichen, Ziffern, Punkt als Dezimaltrennzeichen. Komma, Tausendertrenner und Exponenten ergeben 400.
4. `null`-Elemente in Listen werden im HTTP-Adapter mit 400 abgewiesen, weil System.Text.Json die Nullability von Listenelementen nicht prüft.
5. **Texte strikt nach Vertrag** (`Freitext` in der Domain):
   - Länge in **Unicode-Codepoints**, gemessen **vor** dem Entfernen von Leerraum am Rand (wie `maxLength`).
   - Leerraum wird **ausdrücklich aufgezählt**, nicht über `\s` definiert. `\s` bedeutet in ECMAScript, Python (Schemathesis) und .NET jeweils etwas anderes (z. B. U+0085, U+001C–U+001F, U+FEFF). Die Menge ist die Vereinigung aller Auslegungen: U+0009–U+000D, U+001C–U+001F, U+0020, U+0085, U+00A0, U+1680, U+2000–U+200A, U+2028, U+2029, U+202F, U+205F, U+3000, U+FEFF. Im Vertrag steht sie identisch als Muster `[^…]`.

Gesamtbild: **400** Format · **404** nicht gefunden · **409** Zustand lässt es nicht zu · **422** Eingabe verletzt Fachregel.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Alles bleibt 422 | Einfach | Client kann Eingabefehler nicht von Zustandsproblemen unterscheiden; Schemathesis-Befund bleibt |
| Rezept ohne Zutaten verbieten (`minItems: 1`) | Kein Zustandsfehler beim Skalieren | Rezepte entstehen schrittweise; der Zustandsfall kommt mit Versionen und Komponenten ohnehin wieder |
| Eigene Exception-Typen pro Art | Typsicher | Mehr Klassen ohne Mehrwert; die Art ist eine Eigenschaft derselben Regelverletzung |
| Komma als Dezimaltrennzeichen akzeptieren | Freundlich für deutsche Eingaben | Mehrdeutig („1,000“); Frontends senden maschinenlesbare Zahlen, die Lokalisierung gehört ins Frontend |
| **Regelart + strikte Zahlen** | Eindeutige Semantik, vertragstreu | Etwas mehr Regelwerk |

## Konsequenzen

- **Positiv:** Clients (PWA, Alexa) können gezielt reagieren, z. B. „Füge zuerst Zutaten hinzu“ statt „Eingabe ungültig“. Kein stiller Faktor-10-Fehler mehr.
- **Negativ:** Jede neue Fachregel braucht die bewusste Entscheidung: Eingabe oder Zustand?
- **Absicherung:**
  - `RegelartTests` und die API-Tests (`FehlerstatusApiTests`) prüfen Status, Art und Vorrang.
  - `FreitextTests` prüfen jedes Leerraumzeichen der Menge, die Bereichsgrenzen, die Codepoint-Länge (Emoji, ungepaarte Surrogates) und Leerraum am Rand. API-Tests sichern die gefundenen Fälle deterministisch ab.
  - Einmalig geprüft: Das Vertragsmuster ergibt unter Python `re` für alle 65.536 BMP-Zeichen genau die Domain-Menge.
  - Der Contract-Test verlangt 409 und 422 genau dort, wo der Vertrag sie nennt.
  - Schemathesis (`--checks all`) prüft in der CI, dass vertragsgemäße Anfragen angenommen und vertragswidrige abgewiesen werden.
  - Die Review-Checkliste (`/pr-review`) fragt nach der Regelart neuer Fachregeln.

## Bewusst hingenommen

- **Ungepaarte Surrogates** in JSON-Strings (z. B. `"\ud800"`) ergeben 400, weil System.Text.Json sie ablehnt, obwohl der Vertrag sie formal zulässt. Das ist kein gültiger Unicode-Text, und RFC 8259 (Abschnitt 8.2) lässt das Verhalten offen. Die Domain würde einen solchen Surrogate als einen Codepoint zählen.
- **Mehr als 28 signifikante Stellen** in Zahlen werden von `decimal` gerundet. `1.0000000000000000000000000000001` wird zu 1 g und angenommen, obwohl der Wert nicht `multipleOf: 0.01` ist. Die Abweichung liegt unter 10⁻²⁸ g, und Schemathesis erzeugt solche Werte nicht (Python-Floats haben höchstens 17 Stellen).
