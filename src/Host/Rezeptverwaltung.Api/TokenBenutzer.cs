using System.Text.Json;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Api;

/// <summary>
/// Die angemeldete Person aus dem Access Token des Identity Providers (Zitadel, ADR 0004/0015).
/// <para>
/// Rollen stehen im Claim <c>urn:zitadel:iam:org:project:roles</c> als JSON <c>{ rolle: { betriebId: domain } }</c>.
/// Der Betrieb ergibt sich aus den Rollen-Zuweisungen, nicht aus der Heimat-Organisation der Person: Zugriff
/// gibt es nur dort, wo eine Rolle vergeben ist. Rollen in mehreren Betrieben sind (noch) nicht erlaubt.
/// </para>
/// </summary>
internal sealed class TokenBenutzer(IHttpContextAccessor zugriff) : IAktuellerBenutzer
{
    public const string RollenClaim = "urn:zitadel:iam:org:project:roles";

    /// <summary>
    /// Reserviert für Bestandsdaten aus der Zeit vor den Betrieben (ADR 0015). Kein Token gibt Zugriff darauf;
    /// die Zuordnung zu einem echten Betrieb erfolgt per SQL.
    /// </summary>
    public const string ReservierterBetrieb = "altbestand";

    private static readonly Dictionary<string, Rolle> BekannteRollen = new(StringComparer.Ordinal)
    {
        ["mitarbeiter"] = Rolle.Mitarbeiter,
        ["konditor"] = Rolle.Konditor,
        ["inhaber"] = Rolle.Inhaber,
    };

    private readonly Lazy<(BetriebId? Betrieb, IReadOnlySet<Rolle> Rollen, bool Mehrdeutig)> _auswertung =
        new(() => Auswerten(zugriff.HttpContext?.User.FindAll(RollenClaim).Select(c => c.Value) ?? []));

    public BetriebId? Betrieb => _auswertung.Value.Betrieb;

    public IReadOnlySet<Rolle> Rollen => _auswertung.Value.Rollen;

    public bool BetriebMehrdeutig => _auswertung.Value.Mehrdeutig;

    internal static (BetriebId? Betrieb, IReadOnlySet<Rolle> Rollen, bool Mehrdeutig) Auswerten(IEnumerable<string> claimWerte)
    {
        var zuweisungen = new List<(Rolle Rolle, string Betrieb)>();
        foreach (var wert in claimWerte)
        {
            if (!VersucheZuParsen(wert, out var dokument))
            {
                continue;
            }

            using (dokument)
            {
                if (dokument!.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var rolle in dokument.RootElement.EnumerateObject())
                {
                    if (BekannteRollen.TryGetValue(rolle.Name, out var bekannt) && rolle.Value.ValueKind == JsonValueKind.Object)
                    {
                        zuweisungen.AddRange(rolle.Value.EnumerateObject().Select(betrieb => (bekannt, betrieb.Name)));
                    }
                }
            }
        }

        var betriebe = zuweisungen.Select(z => z.Betrieb).Distinct(StringComparer.Ordinal).ToList();
        if (betriebe.Count != 1 || !IstGueltigeBetriebsId(betriebe[0]) || betriebe[0] == ReservierterBetrieb)
        {
            return (null, new HashSet<Rolle>(), Mehrdeutig: betriebe.Count > 1);
        }

        return (BetriebId.Von(betriebe[0]), zuweisungen.Select(z => z.Rolle).ToHashSet(), Mehrdeutig: false);
    }

    private static bool VersucheZuParsen(string wert, out JsonDocument? dokument)
    {
        try
        {
            dokument = JsonDocument.Parse(wert);
            return true;
        }
        catch (JsonException)
        {
            dokument = null;
            return false;
        }
    }

    private static bool IstGueltigeBetriebsId(string wert)
    {
        try
        {
            _ = BetriebId.Von(wert);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
