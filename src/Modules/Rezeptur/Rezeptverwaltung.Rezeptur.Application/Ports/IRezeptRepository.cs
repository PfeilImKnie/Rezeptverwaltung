using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Application.Ports;

/// <summary>Ausgehender Port: Persistenz für das Aggregat <see cref="Rezept"/>.</summary>
public interface IRezeptRepository
{
    Task HinzufuegenAsync(Rezept rezept, CancellationToken cancellationToken);

    Task<Rezept?> LadenAsync(RezeptId id, CancellationToken cancellationToken);

    /// <summary>
    /// Speichert Änderungen an einem zuvor geladenen Rezept. Hat jemand anderes das Rezept inzwischen geändert,
    /// wird eine Regelverletzung mit Code <c>rezept.gleichzeitig_geaendert</c> (Zustandskonflikt, 409) ausgelöst.
    /// </summary>
    Task SpeichernAsync(Rezept rezept, CancellationToken cancellationToken);

    /// <summary>
    /// Undurchsichtiger Stand eines geladenen oder gespeicherten Rezepts. Ändert sich mit jeder gespeicherten Änderung;
    /// wird als ETag ausgeliefert und bei Änderungen als If-Match zurückerwartet (ADR 0013).
    /// </summary>
    string StandVon(Rezept rezept);
}
