using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Rezeptverwaltung.Rezeptur.Application.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Http;

/// <summary>
/// Eingehender REST-Adapter. Vertrag: api/openapi.yaml. Änderungen immer zuerst dort.
/// Stand eines Rezepts: als ETag in Antworten, als If-Match bei Änderungen am Entwurf (ADR 0013).
/// </summary>
internal static class RezepturEndpoints
{
    public static IEndpointRouteBuilder MapRezeptur(IEndpointRouteBuilder endpoints)
    {
        // Alle Endpunkte verlangen Anmeldung (ADR 0015); welche Rolle was darf, prüfen die Use Cases (403).
        var rezepte = endpoints.MapGroup("/api/v1/rezepte")
            .WithTags("Rezepte")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        rezepte.MapGet("/", RezepteAuflistenAsync)
            .WithName("rezepteAuflisten")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        rezepte.MapGet("/berechtigungen", BerechtigungenAbfragen)
            .WithName("berechtigungenAbfragen");

        rezepte.MapPost("/", RezeptAnlegenAsync)
            .WithName("rezeptAnlegen")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
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

        rezepte.MapGet("/{id:guid}/skaliert/ring", RezeptAufRingSkalierenAsync)
            .WithName("rezeptAufRingSkalieren")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        rezepte.MapGet("/{id:guid}/skaliert/blech", RezeptAufBlechSkalierenAsync)
            .WithName("rezeptAufBlechSkalieren")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        rezepte.MapGet("/{id:guid}/fassungen/{nummer:int:min(1)}", FassungAbfragenAsync)
            .WithName("fassungAbfragen")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var entwurf = rezepte.MapGroup("/{id:guid}/entwurf")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        entwurf.MapPost("/", EntwurfAnlegenAsync).WithName("entwurfAnlegen");

        entwurf.MapPut("/", EntwurfBearbeitenAsync)
            .WithName("entwurfBearbeiten")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        entwurf.MapDelete("/", EntwurfVerwerfenAsync).WithName("entwurfVerwerfen");

        entwurf.MapPost("/freigabe", EntwurfFreigebenAsync).WithName("entwurfFreigeben");

        return endpoints;
    }

    private static async Task<Ok<List<RezeptUebersichtResponse>>> RezepteAuflistenAsync(
        string? suche,
        RezepteAuflistenHandler handler,
        CancellationToken cancellationToken)
    {
        var rezepte = await handler.HandleAsync(suche, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(rezepte.Select(RezeptUebersichtResponse.Aus).ToList());
    }

    private static Ok<BerechtigungenResponse> BerechtigungenAbfragen(BerechtigungenAbfragenHandler handler) =>
        TypedResults.Ok(BerechtigungenResponse.Aus(handler.Handle()));

    private static async Task<Created<RezeptDetailsResponse>> RezeptAnlegenAsync(
        RezeptAnlegenRequest request,
        RezeptAnlegenHandler handler,
        HttpResponse antwort,
        CancellationToken cancellationToken)
    {
        KeineLeerenEintraege(request.Zutaten);
        var befehl = new RezeptAnlegen(request.Name, ZutatRequest.AlsEingaben(request.Zutaten), request.Bezugsform?.AlsForm());

        var details = await handler.HandleAsync(befehl, cancellationToken).ConfigureAwait(false);
        antwort.Headers.ETag = Etag.Aus(details.Stand);
        return TypedResults.Created($"/api/v1/rezepte/{details.Id}", RezeptDetailsResponse.Aus(details));
    }

    private static async Task<Results<Ok<RezeptDetailsResponse>, NotFound>> RezeptAbfragenAsync(
        Guid id,
        RezeptAbfragenHandler handler,
        HttpResponse antwort,
        CancellationToken cancellationToken)
    {
        var details = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);
        if (details is null)
        {
            return TypedResults.NotFound();
        }

        antwort.Headers.ETag = Etag.Aus(details.Stand);
        return TypedResults.Ok(RezeptDetailsResponse.Aus(details));
    }

    private static async Task<Results<Ok<SkaliertesRezeptResponse>, NotFound>> RezeptSkalierenAsync(
        Guid id,
        Gramm zielgewichtGramm,
        int? fassung,
        RezeptSkalierenHandler handler,
        CancellationToken cancellationToken)
    {
        FassungsnummerPruefen(fassung);
        var ergebnis = await handler.HandleAsync(new RezeptSkalieren(id, zielgewichtGramm.Wert, fassung), cancellationToken).ConfigureAwait(false);
        return ergebnis is null ? TypedResults.NotFound() : TypedResults.Ok(SkaliertesRezeptResponse.Aus(ergebnis));
    }

    private static Task<Results<Ok<SkaliertesRezeptResponse>, NotFound>> RezeptAufRingSkalierenAsync(
        Guid id,
        Zentimeter durchmesserCm,
        Zentimeter hoeheCm,
        int? fassung,
        RezeptAufFormSkalierenHandler handler,
        CancellationToken cancellationToken) =>
        AufFormSkalierenAsync(id, Form.Ring(durchmesserCm.Wert, hoeheCm.Wert), fassung, handler, cancellationToken);

    private static Task<Results<Ok<SkaliertesRezeptResponse>, NotFound>> RezeptAufBlechSkalierenAsync(
        Guid id,
        Zentimeter laengeCm,
        Zentimeter breiteCm,
        Zentimeter hoeheCm,
        int? fassung,
        RezeptAufFormSkalierenHandler handler,
        CancellationToken cancellationToken) =>
        AufFormSkalierenAsync(id, Form.Blech(laengeCm.Wert, breiteCm.Wert, hoeheCm.Wert), fassung, handler, cancellationToken);

    private static async Task<Results<Ok<SkaliertesRezeptResponse>, NotFound>> AufFormSkalierenAsync(
        Guid id,
        Form zielform,
        int? fassung,
        RezeptAufFormSkalierenHandler handler,
        CancellationToken cancellationToken)
    {
        FassungsnummerPruefen(fassung);
        var ergebnis = await handler.HandleAsync(new RezeptAufFormSkalieren(id, zielform, fassung), cancellationToken).ConfigureAwait(false);
        return ergebnis is null ? TypedResults.NotFound() : TypedResults.Ok(SkaliertesRezeptResponse.Aus(ergebnis));
    }

    /// <summary>Vertrag: minimum 1. Eine Nummer kleiner 1 ist eine ungültige Anfrage, kein „nicht gefunden“.</summary>
    private static void FassungsnummerPruefen(int? fassung)
    {
        if (fassung < 1)
        {
            throw new BadHttpRequestException("Die Fassungsnummer muss mindestens 1 sein.", StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<Results<Ok<FassungDetailsResponse>, NotFound>> FassungAbfragenAsync(
        Guid id,
        int nummer,
        FassungAbfragenHandler handler,
        HttpResponse antwort,
        CancellationToken cancellationToken)
    {
        var fassung = await handler.HandleAsync(id, nummer, cancellationToken).ConfigureAwait(false);
        return Mit(antwort, fassung);
    }

    private static async Task<Results<Created<FassungDetailsResponse>, NotFound>> EntwurfAnlegenAsync(
        Guid id,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        EntwurfAnlegenHandler handler,
        HttpResponse antwort,
        CancellationToken cancellationToken)
    {
        var entwurf = await handler.HandleAsync(id, Etag.StandAusIfMatch(ifMatch), cancellationToken).ConfigureAwait(false);
        if (entwurf is null)
        {
            return TypedResults.NotFound();
        }

        antwort.Headers.ETag = Etag.Aus(entwurf.Stand);
        return TypedResults.Created($"/api/v1/rezepte/{id}/fassungen/{entwurf.Nummer}", FassungDetailsResponse.Aus(entwurf));
    }

    private static async Task<Results<Ok<FassungDetailsResponse>, NotFound>> EntwurfBearbeitenAsync(
        Guid id,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        EntwurfBearbeitenRequest request,
        EntwurfBearbeitenHandler handler,
        HttpResponse antwort,
        CancellationToken cancellationToken)
    {
        KeineLeerenEintraege(request.Zutaten);
        var befehl = new EntwurfBearbeiten(id, ZutatRequest.AlsEingaben(request.Zutaten), Etag.StandAusIfMatch(ifMatch), request.Bezugsform?.AlsForm());
        return Mit(antwort, await handler.HandleAsync(befehl, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<NoContent, NotFound>> EntwurfVerwerfenAsync(
        Guid id,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        EntwurfVerwerfenHandler handler,
        HttpResponse antwort,
        CancellationToken cancellationToken)
    {
        var stand = await handler.HandleAsync(id, Etag.StandAusIfMatch(ifMatch), cancellationToken).ConfigureAwait(false);
        if (stand is null)
        {
            return TypedResults.NotFound();
        }

        antwort.Headers.ETag = Etag.Aus(stand);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<FassungDetailsResponse>, NotFound>> EntwurfFreigebenAsync(
        Guid id,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        EntwurfFreigebenHandler handler,
        HttpResponse antwort,
        CancellationToken cancellationToken) =>
        Mit(antwort, await handler.HandleAsync(id, Etag.StandAusIfMatch(ifMatch), cancellationToken).ConfigureAwait(false));

    private static Results<Ok<FassungDetailsResponse>, NotFound> Mit(HttpResponse antwort, FassungDetails? fassung)
    {
        if (fassung is null)
        {
            return TypedResults.NotFound();
        }

        antwort.Headers.ETag = Etag.Aus(fassung.Stand);
        return TypedResults.Ok(FassungDetailsResponse.Aus(fassung));
    }

    /// <summary>System.Text.Json prüft Nullability nicht für Listenelemente: [null] muss hier abgewiesen werden.</summary>
    private static void KeineLeerenEintraege(IReadOnlyList<ZutatRequest> zutaten)
    {
        if (zutaten.Any(z => z is null))
        {
            throw new BadHttpRequestException("Die Zutatenliste enthält leere Einträge.", StatusCodes.Status400BadRequest);
        }
    }
}
