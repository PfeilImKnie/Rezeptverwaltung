using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

/// <summary>Feste Betriebe für Tests (ADR 0015).</summary>
internal static class Testbetrieb
{
    public static readonly BetriebId A = BetriebId.Von("betrieb-a");

    public static readonly BetriebId B = BetriebId.Von("betrieb-b");
}

/// <summary>Angemeldete Person für Tests ohne HTTP.</summary>
internal sealed class Testbenutzer(BetriebId? betrieb, bool mehrdeutig = false, params Rolle[] rollen) : IAktuellerBenutzer
{
    public static readonly Testbenutzer InhaberA = new(Testbetrieb.A, false, Rolle.Inhaber);
    public static readonly Testbenutzer KonditorA = new(Testbetrieb.A, false, Rolle.Konditor);
    public static readonly Testbenutzer MitarbeiterA = new(Testbetrieb.A, false, Rolle.Mitarbeiter);
    public static readonly Testbenutzer InhaberB = new(Testbetrieb.B, false, Rolle.Inhaber);
    public static readonly Testbenutzer OhneRolle = new(null);
    public static readonly Testbenutzer ZweiBetriebe = new(null, true);

    public BetriebId? Betrieb { get; } = betrieb;

    public IReadOnlySet<Rolle> Rollen { get; } = rollen.ToHashSet();

    public bool BetriebMehrdeutig { get; } = mehrdeutig;

    public static Testbenutzer Fuer(BetriebId betrieb, params Rolle[] rollen) => new(betrieb, false, rollen);
}
