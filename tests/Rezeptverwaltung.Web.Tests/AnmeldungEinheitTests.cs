extern alias WebHost;

using Anmeldung = WebHost::Rezeptverwaltung.Web.Anmeldung;

namespace Rezeptverwaltung.Web.Tests;

public sealed class AnmeldungEinheitTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/rezepte/neu", "/rezepte/neu")]
    [InlineData("/rezepte?suche=torte", "/rezepte?suche=torte")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("rezepte", "/")]
    [InlineData("//boese.example", "/")]
    [InlineData("/\\boese.example", "/")]
    [InlineData("https://boese.example/", "/")]
    [InlineData("/\r\nSet-Cookie: x", "/")]
    public void Nur_lokale_Ruecksprungadressen(string? eingabe, string erwartet) =>
        Anmeldung.NurLokal(eingabe).ShouldBe(erwartet);
}
