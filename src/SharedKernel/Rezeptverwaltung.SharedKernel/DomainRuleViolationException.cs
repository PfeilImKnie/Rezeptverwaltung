namespace Rezeptverwaltung.SharedKernel;

/// <summary>
/// Verletzung einer fachlichen Regel (Invariante). Der <see cref="Code"/> ist stabil und
/// wird über die API als maschinenlesbarer Fehlercode ausgeliefert.
/// </summary>
public sealed class DomainRuleViolationException : Exception
{
    public DomainRuleViolationException(string code, string message) : base(message) => Code = code;

    public DomainRuleViolationException() : this("domain.unbekannt", "Fachliche Regel verletzt.")
    {
    }

    public DomainRuleViolationException(string message) : this("domain.unbekannt", message)
    {
    }

    public DomainRuleViolationException(string message, Exception innerException) : base(message, innerException) =>
        Code = "domain.unbekannt";

    public string Code { get; }

    public static void Wenn(bool verletzt, string code, string message)
    {
        if (verletzt)
        {
            throw new DomainRuleViolationException(code, message);
        }
    }
}
