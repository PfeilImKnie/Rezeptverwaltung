using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Auswertung des Zitadel-Rollen-Claims (ADR 0015) ohne HTTP.</summary>
public sealed class TokenBenutzerTests
{
    [Fact]
    public void Eine_Rolle_in_einem_Betrieb()
    {
        var (betrieb, rollen, mehrdeutig) = Api.TokenBenutzer.Auswerten(["""{"inhaber":{"123":"a.zitadel.cloud"}}"""]);

        betrieb.ShouldBe(BetriebId.Von("123"));
        rollen.ShouldBe([Rolle.Inhaber]);
        mehrdeutig.ShouldBeFalse();
    }

    [Fact]
    public void Mehrere_Rollen_im_selben_Betrieb_werden_vereint()
    {
        var (_, rollen, _) = Api.TokenBenutzer.Auswerten(["""{"konditor":{"7":"x"},"mitarbeiter":{"7":"x"}}"""]);

        rollen.ShouldBe([Rolle.Konditor, Rolle.Mitarbeiter], ignoreOrder: true);
    }

    [Fact]
    public void Rollen_in_zwei_Betrieben_sind_mehrdeutig_und_ergeben_keinen_Betrieb()
    {
        var (betrieb, rollen, mehrdeutig) = Api.TokenBenutzer.Auswerten(["""{"inhaber":{"1":"a"},"mitarbeiter":{"2":"b"}}"""]);

        betrieb.ShouldBeNull();
        rollen.ShouldBeEmpty();
        mehrdeutig.ShouldBeTrue();
    }

    [Fact]
    public void Unbekannte_Rollen_geben_weder_Recht_noch_Betrieb()
    {
        var (betrieb, rollen, mehrdeutig) = Api.TokenBenutzer.Auswerten(["""{"chef":{"1":"a"},"admin":{"2":"b"}}"""]);

        betrieb.ShouldBeNull();
        rollen.ShouldBeEmpty();
        mehrdeutig.ShouldBeFalse();
    }

    [Theory]
    [InlineData("kein json")]
    [InlineData("[]")]
    [InlineData("\"inhaber\"")]
    [InlineData("""{"inhaber":"1"}""")]
    [InlineData("""{"inhaber":{"ungültig!":"a"}}""")]
    [InlineData("{}")]
    public void Kaputte_oder_unerwartete_Claims_geben_kein_Recht(string claim)
    {
        var (betrieb, rollen, _) = Api.TokenBenutzer.Auswerten([claim]);

        betrieb.ShouldBeNull();
        rollen.ShouldBeEmpty();
    }

    [Fact]
    public void Reservierter_Betrieb_altbestand_ist_ueber_keinen_Token_erreichbar()
    {
        var (betrieb, rollen, _) = Api.TokenBenutzer.Auswerten(["""{"inhaber":{"altbestand":"x"}}"""]);

        betrieb.ShouldBeNull();
        rollen.ShouldBeEmpty();
    }

    [Fact]
    public void Ohne_Claim_kein_Betrieb() =>
        Api.TokenBenutzer.Auswerten([]).Betrieb.ShouldBeNull();

    [Fact]
    public void Rollen_ueber_mehrere_Claims_verteilt_werden_zusammengefuehrt()
    {
        var (betrieb, rollen, _) = Api.TokenBenutzer.Auswerten(["""{"konditor":{"9":"x"}}""", """{"inhaber":{"9":"x"}}"""]);

        betrieb.ShouldBe(BetriebId.Von("9"));
        rollen.ShouldBe([Rolle.Konditor, Rolle.Inhaber], ignoreOrder: true);
    }
}
