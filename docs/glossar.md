# Glossar (Ubiquitous Language)

Fachbegriffe werden im Code **genau so** verwendet (deutsch, siehe ADR 0002).
Neue Begriffe gehören in denselben PR, in dem sie eingeführt werden.

| Begriff | Bedeutung | Im Code |
|---|---|---|
| **Rezept** | Eine Rezeptur mit Namen und Zutatenliste. Konsistenzgrenze (Aggregat). | `Rezept` |
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
| **Grundrezept / Komponente** | Ein Rezept, das als Baustein in anderen Rezepten verwendet wird (Mürbeteig, Ganache …). |
| **Rezeptversion** | Unveränderlicher Stand eines Rezepts; Komponenten verweisen auf eine feste Version. |
| **Ausbeute / Backverlust** | Verhältnis von Fertiggewicht zu Teigeinlage. |
| **Formskalierung** | Skalieren nach Backform (Ring Ø, Rahmen L×B) über die Fläche. |
| **Arbeitssitzung** | Laufende Zubereitung eines Rezepts (aktueller Schritt). Grundlage für die Bedienung per Sprache (Alexa). |
| **Allergene** | Die 14 Hauptallergene nach LMIV, aus den Zutaten über alle Komponenten abgeleitet. |
