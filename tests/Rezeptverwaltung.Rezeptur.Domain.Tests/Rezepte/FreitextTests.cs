using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests.Rezepte;

/// <summary>
/// Freitextregeln müssen exakt dem Vertrag entsprechen (ADR 0009): Leerraum = ausdrücklich aufgezählte Menge aus Freitext.IstLeerraum,
/// Länge in Codepoints vor dem Trimmen. Gefunden von Schemathesis ("\u0085") und im Review (U+001C–U+001F, U+FEFF).
/// </summary>
public sealed class FreitextTests
{
    private static readonly string Emoji = char.ConvertFromUtf32(0x1F382); // Geburtstagstorte, 2 UTF-16-Einheiten

    public static TheoryData<int> Leerraumzeichen => new(
        [.. Enumerable.Range(0x0009, 5), .. Enumerable.Range(0x001C, 4), 0x0020, 0x0085, 0x00A0, 0x1680,
         .. Enumerable.Range(0x2000, 11), 0x2028, 0x2029, 0x202F, 0x205F, 0x3000, 0xFEFF]);

    [Theory]
    [MemberData(nameof(Leerraumzeichen))]
    public void Name_nur_aus_Leerraum_ist_leer(int codepoint) =>
        Should.Throw<DomainRuleViolationException>(() => RezeptName.Von(new string((char)codepoint, 3)))
            .ShouldHaveCode("rezept.name_leer");

    [Theory]
    [MemberData(nameof(Leerraumzeichen))]
    public void Bezeichnung_nur_aus_Leerraum_ist_leer(int codepoint) =>
        Should.Throw<DomainRuleViolationException>(() => Zutatenzeile.Von(new string((char)codepoint, 1), Gewicht.Erfasst(1)))
            .ShouldHaveCode("zutat.bezeichnung_leer");

    [Theory]
    [MemberData(nameof(Leerraumzeichen))]
    public void Leerraum_am_Rand_wird_entfernt(int codepoint)
    {
        var rand = new string((char)codepoint, 1);

        RezeptName.Von(rand + "Sacher" + rand).Wert.ShouldBe("Sacher");
    }

    [Theory]
    [InlineData(0x200B)] // Zero Width Space: kein Leerraum nach \s
    [InlineData(0x0008)] // Grenzen der Leerraum-Bereiche
    [InlineData(0x000E)]
    [InlineData(0x1FFF)]
    [InlineData(0x0000)]
    [InlineData(0x001B)]
    [InlineData(0x0021)]
    [InlineData(0x200B + 1)]
    public void Steuer_und_Sonderzeichen_ausserhalb_von_Leerraum_bleiben_erhalten(int codepoint) =>
        RezeptName.Von(new string((char)codepoint, 1)).Wert.Length.ShouldBe(1);

    [Fact]
    public void Laenge_wird_in_Codepoints_gezaehlt() =>
        RezeptName.Von(string.Concat(Enumerable.Repeat(Emoji, RezeptName.MaximaleLaenge))).Wert.Length
            .ShouldBe(RezeptName.MaximaleLaenge * 2);

    [Fact]
    public void Ein_Codepoint_zu_viel_ist_zu_lang() =>
        Should.Throw<DomainRuleViolationException>(
                () => RezeptName.Von(string.Concat(Enumerable.Repeat(Emoji, RezeptName.MaximaleLaenge + 1))))
            .ShouldHaveCode("rezept.name_zu_lang");

    [Fact]
    public void Bezeichnung_Laenge_wird_in_Codepoints_gezaehlt()
    {
        Zutatenzeile.Von(string.Concat(Enumerable.Repeat(Emoji, Zutatenzeile.MaximaleBezeichnungsLaenge)), Gewicht.Erfasst(1));

        Should.Throw<DomainRuleViolationException>(
                () => Zutatenzeile.Von(string.Concat(Enumerable.Repeat(Emoji, Zutatenzeile.MaximaleBezeichnungsLaenge + 1)), Gewicht.Erfasst(1)))
            .ShouldHaveCode("zutat.bezeichnung_zu_lang");
    }

    [Fact]
    public void Leerraum_am_Rand_zaehlt_zur_Laenge_wie_im_Vertrag() =>
        Should.Throw<DomainRuleViolationException>(() => RezeptName.Von(" " + new string('a', RezeptName.MaximaleLaenge)))
            .ShouldHaveCode("rezept.name_zu_lang");

    [Fact]
    public void Bezeichnung_Leerraum_am_Rand_zaehlt_zur_Laenge_wie_im_Vertrag() =>
        Should.Throw<DomainRuleViolationException>(
                () => Zutatenzeile.Von(new string('x', Zutatenzeile.MaximaleBezeichnungsLaenge) + " ", Gewicht.Erfasst(1)))
            .ShouldHaveCode("zutat.bezeichnung_zu_lang");

    [Fact]
    public void Ungepaarter_Surrogate_zaehlt_als_ein_Codepoint() =>
        RezeptName.Von(new string((char)0xD800, RezeptName.MaximaleLaenge)).Wert.Length.ShouldBe(RezeptName.MaximaleLaenge);
}
