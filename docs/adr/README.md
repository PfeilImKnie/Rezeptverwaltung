# Architecture Decision Records

Neue ADRs entstehen mit dem Skill `/adr` aus der [Vorlage](0000-vorlage.md). Regeln: [ADR 0001](0001-architekturentscheidungen-als-adr.md).

| Nr. | Entscheidung | Status |
|---|---|---|
| [0001](0001-architekturentscheidungen-als-adr.md) | Architekturentscheidungen werden als ADR dokumentiert | Akzeptiert |
| [0002](0002-modularer-monolith-mit-hexagonalen-modulen.md) | Modularer Monolith mit hexagonalen Modulen (DDD) | Akzeptiert |
| [0003](0003-api-first-mit-openapi-vertrag.md) | API-first: `api/openapi.yaml` ist der Vertrag | Akzeptiert, Punkt 5 präzisiert durch 0009 |
| [0004](0004-identity-provider-fuer-oauth2-und-alexa.md) | Externer Identity Provider (OIDC) für Login und Alexa | Akzeptiert, API-Seite umgesetzt (0015) |
| [0005](0005-hosting-auf-hetzner-mit-docker-compose.md) | Hosting auf Hetzner-VM mit Docker Compose | Akzeptiert (Umsetzung ausstehend) |
| [0006](0006-teststrategie.md) | Teststrategie: alles automatisiert verifiziert | Akzeptiert |
| [0007](0007-pr-workflow-test-review-doku.md) | Jeder PR enthält unabhängigen Test, Code-Review und Dokumentation | Akzeptiert, Ausnahme durch 0010 |
| [0008](0008-persistenz-postgresql-mit-ef-core.md) | Persistenz mit PostgreSQL und EF Core | Akzeptiert, umgesetzt (Details 0011) |
| [0009](0009-fehlerstatus-409-und-422-und-strikte-zahlen.md) | Fehlerstatus 422 (Eingabe) / 409 (Zustand), Zahlen und Texte strikt nach Vertrag | Akzeptiert, ergänzt durch 0013 |
| [0010](0010-dependabot-updates-ohne-pr-gate.md) | Reine Dependabot-Updates sind vom PR-Gate ausgenommen | Akzeptiert |
| [0011](0011-abbildung-der-aggregate-in-postgresql.md) | Abbildung der Aggregate in PostgreSQL (JSONB-Listen, Migration beim Start, kein U+0000) | Akzeptiert |
| [0012](0012-lokale-entwicklung-mit-aspire-und-podman-oder-docker.md) | Lokale Entwicklung mit Aspire; Container mit Podman oder Docker | Akzeptiert |
| [0013](0013-rezeptfassungen-mit-entwurf-freigabe-und-stand.md) | Rezeptfassungen mit Entwurf und Freigabe; ETag/If-Match gegen verlorene Änderungen | Akzeptiert |
| [0014](0014-bezugsform-und-umrechnen-ueber-das-volumen.md) | Bezugsform pro Fassung; Umrechnen auf andere Formen über das Volumen | Akzeptiert |
| [0015](0015-betriebe-rollen-und-anmeldung.md) | Betriebe (Mandanten), Rollen und Anmeldung in der API | Akzeptiert |
