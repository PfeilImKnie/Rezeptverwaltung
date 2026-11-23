# 0002 – Modularer Monolith mit hexagonalen Modulen (DDD)

- **Status:** Akzeptiert
- **Datum:** 2026-11-23
- **Betrifft:** gesamte Code-Struktur

## Kontext

Die Fachlichkeit einer Konditorei ist reichhaltig: Grundrezepte als Komponenten, Skalierung nach Gewicht
und Form, Allergene, Versionierung, Kalkulation. Es gibt aber nur sehr wenige Nutzer, und ein einzelner
Entwickler betreibt das System. Gewünscht ist sauberes DDD.

## Entscheidung

1. **Ein Deployable, mehrere Module.** Jeder Bounded Context ist ein Modul unter `src/Modules/<Kontext>/`.
   Geplante Kontexte: *Rezeptur* (Kern), *Rohstoffe*, *Kalkulation*, *Produktion*.
2. **Hexagonal pro Modul**, als getrennte Projekte:
   - `Domain`: Aggregate, Value Objects, Domain Events. **Keine** Framework-Abhängigkeiten.
   - `Application`: Use-Case-Handler und Ports (Interfaces). Kennt nur die Domain.
   - `Infrastructure`: Adapter (REST-Endpoints, Persistenz). Öffentlich ist nur `<Kontext>Module`.
3. **SharedKernel** enthält nur DDD-Bausteine (`Entity`, `AggregateRoot`, `IDomainEvent`, `DomainRuleViolationException`), keine Fachlichkeit.
4. **Module greifen nicht direkt aufeinander zu.** Spätere Kommunikation erfolgt über öffentliche Verträge oder Domain Events.
5. **Sprache:** Fachbegriffe auf Deutsch (`Rezept`, `Gewicht`, `SkaliertAuf`), technische Begriffe auf Englisch (`Repository`, `Handler`, `Async`). Siehe [Glossar](../glossar.md).
6. **Fachregeln** werfen `DomainRuleViolationException` mit stabilem `code`. Die API übersetzt das in 422 Problem Details.
7. **Kein Mediator-Framework.** Handler sind einfache Klassen und werden per DI injiziert. MediatR ist seit v13 kommerziell und bringt hier keinen Mehrwert.

## Betrachtete Alternativen

| Alternative | Pro | Contra |
|---|---|---|
| Microservices | Unabhängiges Deployment | Betriebsaufwand, verteilte Transaktionen; für 1–3 Nutzer absurd |
| Klassische Schichten ohne Module | Einfach | Bounded Contexts verschwimmen, DDD erodiert |
| Vertical Slices ohne Domain-Schicht | Wenig Zeremonie | Fachregeln verteilen sich über Handler; die Fachlichkeit hier ist zu reich dafür |
| **Modularer Monolith, hexagonal** | Klare Grenzen, ein Deployment, Module später extrahierbar | Mehr Projekte, etwas Zeremonie |

## Konsequenzen

- **Positiv:** Die Domain lässt sich isoliert und schnell testen. Ein Wechsel von Persistenz oder Frontend berührt die Domain nicht.
- **Negativ:** Mehr Projekte. Mapping zwischen API-Verträgen, Application-Modellen und Domain.
- **Absicherung:** `ArchitekturTests` (ArchUnitNET) prüfen die Schichtregeln, die Modulgrenzen, dass Infrastruktur nur den Modul-Einstiegspunkt veröffentlicht und dass Aggregate `sealed` sind. Jedes neue Modul muss dort eingetragen werden.
