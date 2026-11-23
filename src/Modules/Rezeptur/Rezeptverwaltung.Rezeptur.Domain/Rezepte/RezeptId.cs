namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

public readonly record struct RezeptId(Guid Wert)
{
    public static RezeptId Neu() => new(Guid.CreateVersion7());

    public override string ToString() => Wert.ToString();
}
