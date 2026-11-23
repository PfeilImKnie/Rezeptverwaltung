using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests;

/// <summary>Gleichheitssemantik der Entity-Basisklasse, geprüft am konkreten Aggregat.</summary>
public sealed class EntityTests
{
    [Fact]
    public void Verschiedene_Rezepte_sind_ungleich()
    {
        var a = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("A"), [], Testzeit.Jetzt);
        var b = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("A"), [], Testzeit.Jetzt);

        a.Equals(b).ShouldBeFalse();
        a.Equals((object?)null).ShouldBeFalse();
    }

    [Fact]
    public void Rezept_ist_gleich_zu_sich_selbst()
    {
        var a = Rezept.Anlegen(Testbetrieb.A, RezeptName.Von("A"), [], Testzeit.Jetzt);

        a.Equals(a).ShouldBeTrue();
        a.GetHashCode().ShouldBe(a.GetHashCode());
    }
}
