namespace Rezeptverwaltung.SharedKernel;

/// <summary>
/// Port: die angemeldete Person in der aktuellen Anfrage (ADR 0015). Implementiert im Host aus dem Token.
/// </summary>
public interface IAktuellerBenutzer
{
    /// <summary>Betrieb, für den die Person handelt; <c>null</c>, wenn er nicht eindeutig ist oder fehlt.</summary>
    BetriebId? Betrieb { get; }

    /// <summary>Rollen der Person in <see cref="Betrieb"/>; leer, wenn es keinen eindeutigen Betrieb gibt.</summary>
    IReadOnlySet<Rolle> Rollen { get; }

    /// <summary><c>true</c>, wenn der Token Rollen in mehreren Betrieben enthält (dann ist <see cref="Betrieb"/> null).</summary>
    bool BetriebMehrdeutig { get; }
}
