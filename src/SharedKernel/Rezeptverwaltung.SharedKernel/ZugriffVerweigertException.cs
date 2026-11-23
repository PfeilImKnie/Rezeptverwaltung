namespace Rezeptverwaltung.SharedKernel;

/// <summary>Angemeldet, aber ohne das nötige Recht oder ohne eindeutigen Betrieb (HTTP 403, ADR 0015).</summary>
public sealed class ZugriffVerweigertException : Exception
{
    public ZugriffVerweigertException()
        : this("recht.fehlt", "Für diese Aktion fehlt das nötige Recht.")
    {
    }

    public ZugriffVerweigertException(string message)
        : this("recht.fehlt", message)
    {
    }

    public ZugriffVerweigertException(string message, Exception innerException)
        : base(message, innerException) => Code = "recht.fehlt";

    public ZugriffVerweigertException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }

    /// <summary>
    /// Liefert den Betrieb der angemeldeten Person, wenn sie mindestens eine der Rollen hat; sonst 403.
    /// </summary>
    public static BetriebId Pruefen(IAktuellerBenutzer benutzer, IReadOnlySet<Rolle> berechtigteRollen, string handlung)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        ArgumentNullException.ThrowIfNull(berechtigteRollen);

        if (benutzer.BetriebMehrdeutig)
        {
            throw new ZugriffVerweigertException(
                "betrieb.nicht_eindeutig",
                "Die Anmeldung gilt für mehrere Betriebe. Bitte mit genau einem Betrieb anmelden.");
        }

        if (benutzer.Betrieb is not { } betrieb || !benutzer.Rollen.Overlaps(berechtigteRollen))
        {
            throw new ZugriffVerweigertException("recht.fehlt", $"Für „{handlung}“ fehlt das nötige Recht.");
        }

        return betrieb;
    }
}
