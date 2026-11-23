using Rezeptverwaltung.Rezeptur.Application.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Http;

// API-Verträge sind bewusst von den Application-Modellen getrennt, damit sich beide unabhängig entwickeln können.

internal sealed record RezeptAnlegenRequest(string Name, IReadOnlyList<ZutatRequest> Zutaten);

internal sealed record ZutatRequest(string Bezeichnung, decimal Gramm);

internal sealed record ZutatResponse(string Bezeichnung, decimal Gramm)
{
    public static ZutatResponse Aus(ZutatMenge zutat) => new(zutat.Bezeichnung, zutat.Gramm);
}

internal sealed record RezeptDetailsResponse(Guid Id, string Name, IReadOnlyList<ZutatResponse> Zutaten, decimal GesamtgewichtGramm)
{
    public static RezeptDetailsResponse Aus(RezeptDetails details) => new(
        details.Id,
        details.Name,
        details.Zutaten.Select(ZutatResponse.Aus).ToList(),
        details.GesamtgewichtGramm);
}

internal sealed record SkaliertesRezeptResponse(Guid RezeptId, string Name, decimal ZielgewichtGramm, IReadOnlyList<ZutatResponse> Zutaten)
{
    public static SkaliertesRezeptResponse Aus(SkaliertesRezept rezept) => new(
        rezept.RezeptId,
        rezept.Name,
        rezept.ZielgewichtGramm,
        rezept.Zutaten.Select(ZutatResponse.Aus).ToList());
}
