# 0014 – Bezugsform pro Fassung; Umrechnen auf andere Formen über das Volumen

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** Modul Rezeptur (Domain, Application, Persistenz, API)

## Kontext

Fachliche Vorgaben der Konditormeisterin (geklärt am 2026-11-23):

- Jedes Rezept ist für eine Form geschrieben, einen **Ring** oder ein **Blech/Rahmen**, damit es auf andere Größen umgerechnet werden kann.
- **Die Höhe wird in cm mit angegeben** und zählt beim Umrechnen. Ring 26 × 6 cm braucht weniger Masse als 26 × 8 cm.
- Komponenten ohne Form (Cremes, Ganache …) gehen später in **Gramm** in ein Endprodukt ein. Sie haben also keine Bezugsform.

## Entscheidung

1. **Bezugsform pro Fassung, optional.**
   - Erlaubt sind `Ring(Durchmesser, Höhe)` und `Blech(Länge, Breite, Höhe)`. Der Rahmen ist ein Blech.
   - Maße in cm auf **0,1 cm** genau, Durchmesser/Länge/Breite **1–200 cm**, Höhe **0,1–50 cm**. Verstöße ergeben 422 mit den Codes `form.mass_ungueltig`, `form.hoehe_ungueltig` und `form.zu_genau`. Die Meldung nennt das betroffene Maß.
   - Eine andere Form bedeutet eine neue Fassung (ADR 0013). Ein neuer Entwurf übernimmt die Form, freigegebene Fassungen behalten ihre.
2. **Umrechnen über das Volumen.**
   - Faktor = V(Zielform) / V(Bezugsform), mit V(Ring) = π · r² · h und V(Blech) = L · B · h.
   - Ring ↔ Blech ist möglich.
   - Ohne Bezugsform → 409 `fassung.ohne_bezugsform`. Reihenfolge: ungültige Zielform 422 vor 409.
   - Intern wird ungerundet gerechnet. In der Ausgabe wird der Faktor auf **12 signifikante Stellen** gerundet, weil π sonst Rechenrauschen erzeugt (4,0000000000000000000000000004). Signifikante statt Nachkommastellen, damit auch sehr kleine Faktoren (z. B. 0,0000001) nie als 0 erscheinen; der Vertrag sagt `faktor > 0`. Die Grammangaben werden wie bisher auf 0,01 g gerundet.
3. **API:**
   - Zwei eigene Endpunkte, `GET …/skaliert/ring` und `GET …/skaliert/blech`, nur mit Pflichtparametern. So bleibt der Vertrag eindeutig, statt Parameter zu haben, die voneinander abhängen.
   - Die Form ist im Vertrag ein `oneOf` mit dem Diskriminator `art`. Maße, die zur Art nicht passen, werden ignoriert.
   - `faktor` und `zielform` sind neue Felder der Skalierungsantwort.
   - **`PUT …/entwurf` ersetzt den Inhalt vollständig:** Ohne `bezugsform` hat der Entwurf danach keine Form. Ein Client hat wegen `If-Match` den aktuellen Stand samt Form gelesen und schickt sie mit.
4. **Speicherung:**
   - Im JSON der Fassung unter `bezugsform` (`art`, `durchmesserCm` | `laengeCm`, `breiteCm`, `hoeheCm`).
   - Keine Migration: Fassungen ohne diesen Schlüssel laden als „ohne Form“.
   - Jede Fassung besitzt eine eigene Instanz der Form, wie bei den Zutatenzeilen.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Nur über die Fläche umrechnen | Einfacher | Fachlich falsch, sobald Formen verschiedene Höhen haben (Vorgabe: die Höhe zählt) |
| Bezugsform am Rezept statt an der Fassung | Weniger Daten | Eine andere Form würde alte Fassungen still „umdeuten“ (widerspricht ADR 0013) |
| Ein Endpunkt `…/skaliert/form?art=…&…` | Weniger Pfade | Parameter hängen von `art` ab, das ist im Vertrag nicht prüfbar; Schemathesis erzeugt laufend ungültige Kombinationen |
| Weitere Formen (Herz, Achteck, Quadrat) | Vollständiger | Kein Bedarf benannt; ein Quadrat ist ein Blech mit L = B. Lässt sich später als weitere `art` ergänzen |
| `PUT` ohne Feld = Form behalten | Bequem für Clients, die das Feld nicht kennen | „Weglassen“ und „entfernen“ wären nicht unterscheidbar bzw. bräuchten Sonderlogik; `PUT` ist nach HTTP eine vollständige Ersetzung |

## Konsequenzen

- **Positiv:**
  - Rezepte lassen sich auf jede Ring- und Blechgröße umrechnen, auch zwischen Ring und Blech.
  - Alte Fassungen behalten ihre Form.
  - Für Komponenten in Gramm (Cremes) ist der Weg frei.
- **Negativ:** Clients müssen beim Bearbeiten des Entwurfs die Form mitschicken, sonst wird sie entfernt.
- **Absicherung:**
  - `BezugsformTests` und `BezugsformGrenzfallTests` decken Grenzwerte, Genauigkeit und Meldungen ab. Property-Tests prüfen: gleiche Form → 1, hin und zurück → 1, Verkettung, Höhe linear, Verhältnisse bleiben.
  - `FassungMitFormTests` prüft Übernahme und Unveränderlichkeit.
  - `BezugsformApiTests` und `BezugsformGrenzfallApiTests` prüfen 400/404/409/422, die Kompatibilität bisheriger Clients und dass der Faktor ohne Rechenrauschen ausgegeben wird.
  - `BezugsformPersistenzTests` prüfen das Speicherformat und Altdaten ohne Form.
