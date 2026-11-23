namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

public enum FassungsStatus
{
    /// <summary>In Arbeit: darf geändert werden, gilt aber noch nicht.</summary>
    Entwurf,

    /// <summary>Gilt und ist unveränderlich.</summary>
    Freigegeben,
}
