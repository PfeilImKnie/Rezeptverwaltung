namespace Rezeptverwaltung.SharedKernel;

/// <summary>
/// Die Änderung beruht nicht auf dem aktuellen Stand (ADR 0013): Der Stand fehlt (HTTP 428) oder ist veraltet (HTTP 412).
/// Schützt davor, dass ein Gerät still die Änderung eines anderen überschreibt.
/// </summary>
public sealed class VorbedingungException : Exception
{
    public VorbedingungException()
        : this(fehlt: false)
    {
    }

    public VorbedingungException(string message)
        : base(message) => Code = "anfrage.stand_veraltet";

    public VorbedingungException(string message, Exception innerException)
        : base(message, innerException) => Code = "anfrage.stand_veraltet";

    private VorbedingungException(bool fehlt, string codeVeraltet = "anfrage.stand_veraltet")
        : base(fehlt
            ? "Für Änderungen wird der zuletzt gelesene Stand benötigt (If-Match)."
            : "Die Daten wurden inzwischen geändert. Bitte neu laden und erneut versuchen.")
    {
        Fehlt = fehlt;
        Code = fehlt ? "anfrage.if_match_fehlt" : codeVeraltet;
    }

    /// <summary><c>true</c>: kein Stand angegeben (428); <c>false</c>: Stand veraltet (412).</summary>
    public bool Fehlt { get; }

    public string Code { get; }

    /// <summary>
    /// Prüft, ob die Änderung auf dem aktuellen Stand beruht. <c>null</c> bedeutet: kein Stand angegeben (428).
    /// </summary>
    /// <param name="codeVeraltet">Stabiler, modulbezogener Code für „veraltet“, z. B. <c>rezept.veraltet</c>.</param>
    public static void Pruefen(string aktuellerStand, string? erwarteterStand, string codeVeraltet)
    {
        if (erwarteterStand is null)
        {
            throw new VorbedingungException(fehlt: true);
        }

        if (!string.Equals(aktuellerStand, erwarteterStand, StringComparison.Ordinal))
        {
            throw new VorbedingungException(fehlt: false, codeVeraltet);
        }
    }
}
