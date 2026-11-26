namespace Rezeptverwaltung.Web.Client.Api;

/// <summary>Fehlerantwort der API (Problem Details) in für Menschen lesbarer Form.</summary>
public sealed class ApiFehlerException : Exception
{
    public ApiFehlerException()
    {
    }

    public ApiFehlerException(string message)
        : base(message)
    {
    }

    public ApiFehlerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ApiFehlerException(int status, string? code, string message)
        : base(message)
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }

    /// <summary>Stabiler Fehlercode der API (z. B. <c>gewicht.zu_genau</c>), falls vorhanden.</summary>
    public string? Code { get; }

    /// <summary>Das Rezept wurde inzwischen an anderer Stelle geändert (412): neu laden.</summary>
    public bool IstVeraltet => Status == 412 || Code == "rezept.gleichzeitig_geaendert";
}
