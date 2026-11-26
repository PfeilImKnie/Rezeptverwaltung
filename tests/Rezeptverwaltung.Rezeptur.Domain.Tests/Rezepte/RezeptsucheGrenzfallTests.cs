using CsCheck;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

/// <summary>
/// Unabhängiger Test der Rezeptsuche (Anforderung: Groß-/Kleinschreibung und Akzente egal, höchstens 100 Zeichen,
/// alphabetisch mit Umlauten wie dem Grundbuchstaben): Grenzwerte und Eigenschaften.
/// </summary>
public sealed class RezeptsucheGrenzfallTests
{
    private static readonly Gen<char> Buchstabe = Gen.Char["abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZäöüÄÖÜéèêÉ"];

    private static readonly Gen<string> Name = Buchstabe.Array[1, 40].Select(z => new string(z));

    [Fact]
    public void Suchtext_mit_genau_100_Zeichen_ist_gueltig() =>
        Rezeptsuche.Von(new string('a', Rezeptsuche.MaximaleLaenge)).Text.Length.ShouldBe(100);

    [Fact]
    public void Suchtext_mit_101_Emoji_ist_zu_lang() =>
        Should.Throw<DomainRuleViolationException>(() => Rezeptsuche.Von(string.Concat(Enumerable.Repeat("🎂", 101))))
            .ShouldHaveCode("suche.zu_lang");

    [Fact]
    public void Suchtext_aus_100_Leerzeichen_ist_gueltig_und_findet_alles()
    {
        var suche = Rezeptsuche.Von(new string(' ', 100));

        suche.Text.ShouldBeEmpty();
        suche.Passt(RezeptName.Von("Sachertorte")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("kase")]
    [InlineData("Käse")]
    [InlineData("KÄSE")]
    public void Zerlegt_geschriebener_Umlaut_im_Namen_wird_gefunden(string suche) =>
        Rezeptsuche.Von(suche).Passt(RezeptName.Von("Käsekuchen")).ShouldBeTrue();

    [Fact]
    public void Zerlegt_geschriebener_Umlaut_in_der_Suche_findet_den_Namen() =>
        Rezeptsuche.Von("käse").Passt(RezeptName.Von("Käsekuchen")).ShouldBeTrue();

    [Fact]
    public void Suche_nach_Umlaut_findet_den_Grundbuchstaben_in_Grossschrift() =>
        Rezeptsuche.Von("öl").Passt(RezeptName.Von("OLIVENÖLKUCHEN")).ShouldBeTrue();

    [Fact]
    public void Umlaute_am_Wortanfang_werden_wie_der_Grundbuchstabe_einsortiert()
    {
        string[] roh = ["Zimtsterne", "Orangencreme", "Ölbiskuit", "Überraschungstorte", "Ulmer Brot"];

        roh.Select(RezeptName.Von).Order(Rezeptsuche.Reihenfolge).Select(n => n.Wert)
            .ShouldBe(["Ölbiskuit", "Orangencreme", "Überraschungstorte", "Ulmer Brot", "Zimtsterne"]);
    }

    [Fact]
    public void Jeder_Teil_eines_Namens_findet_ihn_in_jeder_Schreibweise() =>
        Gen.Select(Name, Gen.Int[0, 39], Gen.Int[1, 40], Gen.Bool).Sample((name, start, laenge, gross) =>
        {
            start = Math.Min(start, name.Length - 1);
            laenge = Math.Min(laenge, name.Length - start);
            var teil = name.Substring(start, laenge);
            var suche = gross ? teil.ToUpperInvariant() : teil.ToLowerInvariant();

            return Rezeptsuche.Von(suche).Passt(RezeptName.Von(name));
        });

    [Fact]
    public void Leerraum_am_Rand_aendert_das_Ergebnis_nicht() =>
        Gen.Select(Name, Name).Sample((suche, name) =>
            Rezeptsuche.Von($" \t{suche}\n ").Passt(RezeptName.Von(name)) == Rezeptsuche.Von(suche).Passt(RezeptName.Von(name)));

    [Fact]
    public void Reihenfolge_ist_antisymmetrisch_und_nur_bei_gleichem_Namen_unentschieden() =>
        Gen.Select(Name, Name).Sample((a, b) =>
        {
            var vergleich = Math.Sign(Rezeptsuche.Reihenfolge.Compare(RezeptName.Von(a), RezeptName.Von(b)));
            var umgekehrt = Math.Sign(Rezeptsuche.Reihenfolge.Compare(RezeptName.Von(b), RezeptName.Von(a)));

            return vergleich == -umgekehrt && (vergleich == 0) == (a == b);
        });

    [Fact]
    public void Reihenfolge_ist_transitiv() =>
        Gen.Select(Name, Name, Name).Sample((a, b, c) =>
        {
            var sortiert = new[] { a, b, c }.Select(RezeptName.Von).Order(Rezeptsuche.Reihenfolge).ToArray();

            return Rezeptsuche.Reihenfolge.Compare(sortiert[0], sortiert[2]) <= 0;
        });

    [Fact]
    public void Sortierte_Liste_haengt_nicht_von_der_Eingabereihenfolge_ab() =>
        Name.List[0, 20].Sample(namen =>
        {
            var vorwaerts = namen.Select(RezeptName.Von).Order(Rezeptsuche.Reihenfolge).Select(n => n.Wert);
            var rueckwaerts = Enumerable.Reverse(namen).Select(RezeptName.Von).Order(Rezeptsuche.Reihenfolge).Select(n => n.Wert);

            return vorwaerts.SequenceEqual(rueckwaerts);
        });
}
