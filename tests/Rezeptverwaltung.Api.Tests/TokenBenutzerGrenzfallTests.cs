using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Grenzfälle der Auswertung des Zitadel-Rollen-Claims (ADR 0015), abgeleitet aus „unbekannte Rollen geben kein Recht“.</summary>
public sealed class TokenBenutzerGrenzfallTests
{
    [Theory]
    [InlineData("Inhaber")]
    [InlineData("INHABER")]
    [InlineData(" inhaber")]
    [InlineData("inhaber ")]
    [InlineData("inhaberin")]
    [InlineData("Inhaber/in")]
    public void Rollennamen_gelten_nur_in_exakter_Schreibweise(string rolle)
    {
        var (betrieb, rollen, mehrdeutig) = Api.TokenBenutzer.Auswerten(["{\"" + rolle + "\":{\"1\":\"a\"}}"]);

        betrieb.ShouldBeNull();
        rollen.ShouldBeEmpty();
        mehrdeutig.ShouldBeFalse();
    }

    [Fact]
    public void Unbekannte_Rolle_in_einem_anderen_Betrieb_macht_den_Betrieb_nicht_mehrdeutig()
    {
        var (betrieb, rollen, mehrdeutig) = Api.TokenBenutzer.Auswerten(["""{"mitarbeiter":{"1":"a"},"chef":{"2":"b"}}"""]);

        betrieb.ShouldBe(BetriebId.Von("1"));
        rollen.ShouldBe([Rolle.Mitarbeiter]);
        mehrdeutig.ShouldBeFalse();
    }

    [Fact]
    public void Unbekannte_Rolle_im_selben_Betrieb_gibt_kein_zusaetzliches_Recht()
    {
        var (betrieb, rollen, _) = Api.TokenBenutzer.Auswerten(["""{"mitarbeiter":{"1":"a"},"admin":{"1":"a"}}"""]);

        betrieb.ShouldBe(BetriebId.Von("1"));
        rollen.ShouldBe([Rolle.Mitarbeiter]);
    }

    [Fact]
    public void Dieselbe_Rolle_in_zwei_Betrieben_ist_mehrdeutig()
    {
        var (betrieb, rollen, mehrdeutig) = Api.TokenBenutzer.Auswerten(["""{"mitarbeiter":{"1":"a","2":"b"}}"""]);

        betrieb.ShouldBeNull();
        rollen.ShouldBeEmpty();
        mehrdeutig.ShouldBeTrue();
    }

    [Fact]
    public void Zwei_Betriebe_ueber_zwei_Claims_verteilt_sind_mehrdeutig()
    {
        var (betrieb, _, mehrdeutig) = Api.TokenBenutzer.Auswerten(["""{"inhaber":{"1":"a"}}""", """{"mitarbeiter":{"2":"b"}}"""]);

        betrieb.ShouldBeNull();
        mehrdeutig.ShouldBeTrue();
    }

    [Fact]
    public void Rolle_ohne_Betrieb_gibt_kein_Recht()
    {
        var (betrieb, rollen, mehrdeutig) = Api.TokenBenutzer.Auswerten(["""{"inhaber":{}}"""]);

        betrieb.ShouldBeNull();
        rollen.ShouldBeEmpty();
        mehrdeutig.ShouldBeFalse();
    }

    [Fact]
    public void Betriebs_ID_ueber_64_Zeichen_gibt_kein_Recht()
    {
        var (betrieb, rollen, _) = Api.TokenBenutzer.Auswerten(["{\"inhaber\":{\"" + new string('1', 65) + "\":\"a\"}}"]);

        betrieb.ShouldBeNull();
        rollen.ShouldBeEmpty();
    }
}
