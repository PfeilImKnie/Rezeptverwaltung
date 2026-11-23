using CsCheck;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

/// <summary>Zufallsgeneratoren für fachlich gültige Werte (Property-based Testing mit CsCheck).</summary>
internal static class Generatoren
{
    /// <summary>Gültig erfasste Gewichte: 0,01 g bis 100 kg, auf 0,01 g genau.</summary>
    public static readonly Gen<Gewicht> ErfasstesGewicht =
        Gen.Int[1, 10_000_000].Select(hundertstel => Gewicht.Erfasst(hundertstel / 100m));

    public static readonly Gen<string> Bezeichnung =
        Gen.String[Gen.Char.AlphaNumeric, 1, 30];

    public static readonly Gen<Zutatenzeile> Zutatenzeile =
        Gen.Select(Bezeichnung, ErfasstesGewicht, Domain.Rezepte.Zutatenzeile.Von);

    public static readonly Gen<Rezept> RezeptMitZutaten =
        Zutatenzeile.List[1, 25].Select(zeilen => Rezept.Anlegen(RezeptName.Von("Testrezept"), zeilen, Testzeit.Jetzt));
}
