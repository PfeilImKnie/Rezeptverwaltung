using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

public sealed record RezeptName
{
    public const int MaximaleLaenge = 200;

    private RezeptName(string wert) => Wert = wert;

    public string Wert { get; }

    public static RezeptName Von(string? wert)
    {
        var roh = wert ?? string.Empty;
        DomainRuleViolationException.Wenn(
            Freitext.Laenge(roh) > MaximaleLaenge,
            "rezept.name_zu_lang",
            $"Ein Rezeptname darf höchstens {MaximaleLaenge} Zeichen lang sein.");
        var bereinigt = Freitext.OhneLeerraumAmRand(roh);
        DomainRuleViolationException.Wenn(bereinigt.Length == 0, "rezept.name_leer", "Ein Rezept braucht einen Namen.");
        return new RezeptName(bereinigt);
    }

    public override string ToString() => Wert;
}
