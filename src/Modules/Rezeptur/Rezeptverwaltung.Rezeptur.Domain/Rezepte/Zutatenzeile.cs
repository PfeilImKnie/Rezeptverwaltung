using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Rezepte;

/// <summary>
/// Eine Zeile der Zutatenliste, z. B. „Butter – 250 g“.
/// Die Bezeichnung ist vorerst Freitext; später verweist sie auf eine Zutat im Kontext „Rohstoffe“.
/// Dieselbe Zutat darf mehrfach vorkommen (z. B. Zucker für Teig und für Eischnee).
/// </summary>
public sealed record Zutatenzeile
{
    public const int MaximaleBezeichnungsLaenge = 200;

    private Zutatenzeile(string bezeichnung, Gewicht menge)
    {
        Bezeichnung = bezeichnung;
        Menge = menge;
    }

    public string Bezeichnung { get; }

    public Gewicht Menge { get; }

    public static Zutatenzeile Von(string? bezeichnung, Gewicht menge)
    {
        var bereinigt = bezeichnung?.Trim() ?? string.Empty;
        DomainRuleViolationException.Wenn(bereinigt.Length == 0, "zutat.bezeichnung_leer", "Eine Zutat braucht eine Bezeichnung.");
        DomainRuleViolationException.Wenn(
            bereinigt.Length > MaximaleBezeichnungsLaenge,
            "zutat.bezeichnung_zu_lang",
            $"Eine Zutatenbezeichnung darf höchstens {MaximaleBezeichnungsLaenge} Zeichen lang sein.");
        DomainRuleViolationException.Wenn(menge.IstNull, "zutat.menge_null", "Eine Zutat muss eine Menge größer als 0 g haben.");
        return new Zutatenzeile(bereinigt, menge);
    }

    public Zutatenzeile Skaliert(Skalierungsfaktor faktor) => new(Bezeichnung, Menge.Skaliert(faktor));
}
