# Glossar (Ubiquitous Language)

Fachbegriffe werden im Code **genau so** verwendet (deutsch, siehe ADR 0002).
Neue Begriffe gehören in denselben PR, in dem sie eingeführt werden.

| Begriff | Bedeutung | Im Code |
|---|---|---|
| **Rezept** | Eine Rezeptur mit Namen und ihren Fassungen. Konsistenzgrenze (Aggregat). | `Rezept` |
| **Fassung** | Ein Stand eines Rezepts mit eigener Zutatenliste, fortlaufend nummeriert (1, 2, 3 …). Nummern werden nie wiederverwendet, auch nicht nach dem Verwerfen eines Entwurfs. | `Fassung` |
| **Entwurf** | Fassung in Arbeit: darf geändert werden, gilt aber noch nicht. Es gibt höchstens einen. Ein neues Rezept beginnt mit Fassung 1 als Entwurf; ein späterer Entwurf entsteht als Kopie der aktuellen Fassung. | `FassungsStatus.Entwurf` |
| **Freigabe** | Macht den Entwurf zur geltenden Fassung. Danach ist die Fassung unveränderlich. Eine Fassung ohne Zutaten kann nicht freigegeben werden. | `Rezept.EntwurfFreigeben` |
| **Verwerfen** | Löscht den Entwurf. Nicht möglich, wenn er die einzige Fassung des Rezepts ist. | `Rezept.EntwurfVerwerfen` |
| **Aktuelle Fassung** | Die neueste freigegebene Fassung. | `Rezept.AktuelleFassung` |
| **Maßgebliche Fassung** | Die Fassung, die ohne ausdrückliche Angabe gilt: die aktuelle Fassung, solange es keine gibt, der Entwurf. | `Rezept.MassgeblicheFassung` |
| **Zutatenzeile** | Eine Zeile der Zutatenliste: Bezeichnung + Menge. Dieselbe Zutat darf mehrfach vorkommen (z. B. Zucker im Teig und im Eischnee). | `Zutatenzeile` |
| **Gewicht** | Menge in Gramm. In der Konditorei wird ausschließlich nach Gewicht gearbeitet. | `Gewicht` |
| **Erfasstes Gewicht** | Ein von einem Menschen eingegebenes Gewicht: 0 bis 1.000.000.000 g, auf 0,01 g genau. | `Gewicht.Erfasst` |
| **Gesamtgewicht** | Summe aller Zutatenmengen eines Rezepts (Teigeinlage, vor Backverlust). | `Rezept.Gesamtgewicht` |
| **Skalieren** | Alle Mengen mit demselben Faktor umrechnen. Das Rezept selbst bleibt unverändert. | `Rezept.Skaliert`, `Rezept.SkaliertAuf` |
| **Skalierungsfaktor** | Faktor > 0, mit dem alle Mengen multipliziert werden. | `Skalierungsfaktor` |
| **Zielgewicht** | Gewünschtes Gesamtgewicht nach dem Skalieren. | `zielgewichtGramm` |
| **Ungültige Eingabe** | Eine Angabe verletzt eine Fachregel (z. B. Gewicht genauer als 0,01 g). Die Eingabe muss korrigiert werden. HTTP 422. | `Regelart.UngueltigeEingabe` |
| **Zustandskonflikt** | Die Angabe ist gültig, aber das Rezept ist (noch) nicht in einem passenden Zustand (z. B. ohne Zutaten skalieren). Erst das Rezept vervollständigen. HTTP 409. | `Regelart.Zustandskonflikt` |

## Geplante Begriffe (noch nicht im Code)

| Begriff | Bedeutung |
|---|---|
| **Grundrezept / Komponente** | Ein Rezept, das als Baustein in anderen Rezepten verwendet wird (Mürbeteig, Ganache …), immer in einer **festen Fassung**. Umstellen auf eine neuere Fassung ist eine bewusste Änderung. Weitere geeignete Varianten sind nur eine Notiz am Endprodukt. |
| **Anteil** | Menge einer Komponente mit anderer Bezugsform, z. B. „¼ Blech“ Biskuit für eine Torte im Ring Ø 26. |
| **Zubereitung** | Protokoll, wann ein Rezept in welcher Fassung und Größe zubereitet wurde, mit den Fassungen aller Komponenten. Wird nie nachträglich verändert. |
| **Ausbeute / Backverlust** | Verhältnis von Fertiggewicht zu Teigeinlage. |
| **Formskalierung** | Skalieren nach Backform (Ring Ø, Rahmen L×B) über die Fläche. |
| **Arbeitssitzung** | Laufende Zubereitung eines Rezepts (aktueller Schritt). Grundlage für die Bedienung per Sprache (Alexa). |
| **Allergene** | Die 14 Hauptallergene nach LMIV, aus den Zutaten über alle Komponenten abgeleitet. |
