namespace Rezeptverwaltung.SharedKernel;

/// <summary>Art einer verletzten Fachregel. Bestimmt den HTTP-Status (ADR 0009).</summary>
public enum Regelart
{
    /// <summary>Die Eingabe selbst ist fachlich ungültig (z. B. Gewicht zu genau). HTTP 422.</summary>
    UngueltigeEingabe,

    /// <summary>Die Eingabe ist gültig, aber der Zustand des Aggregats lässt die Operation nicht zu. HTTP 409.</summary>
    Zustandskonflikt,
}

/// <summary>
/// Verletzung einer fachlichen Regel (Invariante). Der <see cref="Code"/> ist stabil und
/// wird über die API als maschinenlesbarer Fehlercode ausgeliefert.
/// </summary>
public sealed class DomainRuleViolationException : Exception
{
    public DomainRuleViolationException(string code, string message, Regelart art)
        : base(message)
    {
        Code = code;
        Art = art;
    }

    public DomainRuleViolationException(string code, string message)
        : this(code, message, Regelart.UngueltigeEingabe)
    {
    }

    public DomainRuleViolationException()
        : this("domain.unbekannt", "Fachliche Regel verletzt.")
    {
    }

    public DomainRuleViolationException(string message)
        : this("domain.unbekannt", message)
    {
    }

    public DomainRuleViolationException(string message, Exception innerException)
        : base(message, innerException) =>
        Code = "domain.unbekannt";

    public string Code { get; }

    public Regelart Art { get; }

    /// <summary>Ungültige Eingabe, wenn <paramref name="verletzt"/>.</summary>
    public static void Wenn(bool verletzt, string code, string message)
    {
        if (verletzt)
        {
            throw new DomainRuleViolationException(code, message, Regelart.UngueltigeEingabe);
        }
    }

    /// <summary>Zustandskonflikt, wenn <paramref name="verletzt"/>.</summary>
    public static void ZustandWenn(bool verletzt, string code, string message)
    {
        if (verletzt)
        {
            throw new DomainRuleViolationException(code, message, Regelart.Zustandskonflikt);
        }
    }
}
