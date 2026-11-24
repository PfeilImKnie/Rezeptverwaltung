namespace Rezeptverwaltung.SharedKernel;

/// <summary>Rollen innerhalb eines Betriebs (ADR 0015). Welche Rolle welches Recht hat, legt jedes Modul fest.</summary>
public enum Rolle
{
    /// <summary>Liest und rechnet um (z. B. am Tablet in der Backstube).</summary>
    Mitarbeiter,

    /// <summary>Legt Rezepte an und bearbeitet Entwürfe.</summary>
    Konditor,

    /// <summary>Darf alles, auch freigeben.</summary>
    Inhaber,
}
