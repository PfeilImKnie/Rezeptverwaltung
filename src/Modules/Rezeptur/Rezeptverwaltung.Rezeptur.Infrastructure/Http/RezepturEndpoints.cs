using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Rezeptverwaltung.Rezeptur.Application.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Http;

/// <summary>
/// Eingehender REST-Adapter. Vertrag: api/openapi.yaml. Änderungen immer zuerst dort.
/// </summary>
internal static class RezepturEndpoints
{
    public static IEndpointRouteBuilder MapRezeptur(IEndpointRouteBuilder endpoints)
    {
        var rezepte = endpoints.MapGroup("/api/v1/rezepte").WithTags("Rezepte");

        rezepte.MapPost("/", RezeptAnlegenAsync)
            .WithName("rezeptAnlegen")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        rezepte.MapGet("/{id:guid}", RezeptAbfragenAsync)
            .WithName("rezeptAbfragen")
            .ProducesProblem(StatusCodes.Status404NotFound);

        rezepte.MapGet("/{id:guid}/skaliert", RezeptSkalierenAsync)
            .WithName("rezeptSkalieren")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<Created<RezeptDetailsResponse>> RezeptAnlegenAsync(
        RezeptAnlegenRequest request,
        RezeptAnlegenHandler handler,
        CancellationToken cancellationToken)
    {
        // System.Text.Json prüft Nullability nicht für Listenelemente: [null] muss hier abgewiesen werden.
        if (request.Zutaten.Any(z => z is null))
        {
            throw new BadHttpRequestException("Die Zutatenliste enthält leere Einträge.", StatusCodes.Status400BadRequest);
        }

        var befehl = new RezeptAnlegen(
            request.Name,
            request.Zutaten.Select(z => new ZutatEingabe(z.Bezeichnung, z.Gramm)).ToList());

        var details = await handler.HandleAsync(befehl, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/rezepte/{details.Id}", RezeptDetailsResponse.Aus(details));
    }

    private static async Task<Results<Ok<RezeptDetailsResponse>, NotFound>> RezeptAbfragenAsync(
        Guid id,
        RezeptAbfragenHandler handler,
        CancellationToken cancellationToken)
    {
        var details = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);
        return details is null ? TypedResults.NotFound() : TypedResults.Ok(RezeptDetailsResponse.Aus(details));
    }

    private static async Task<Results<Ok<SkaliertesRezeptResponse>, NotFound>> RezeptSkalierenAsync(
        Guid id,
        Gramm zielgewichtGramm,
        RezeptSkalierenHandler handler,
        CancellationToken cancellationToken)
    {
        var ergebnis = await handler.HandleAsync(new RezeptSkalieren(id, zielgewichtGramm.Wert), cancellationToken).ConfigureAwait(false);
        return ergebnis is null ? TypedResults.NotFound() : TypedResults.Ok(SkaliertesRezeptResponse.Aus(ergebnis));
    }
}
