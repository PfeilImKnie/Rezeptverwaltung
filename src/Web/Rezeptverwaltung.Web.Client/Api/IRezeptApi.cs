namespace Rezeptverwaltung.Web.Client.Api;

/// <summary>Zugriff auf die Rezeptur-API über den eigenen Server. Fehler kommen als <see cref="ApiFehlerException"/>.</summary>
public interface IRezeptApi
{
    Task<IReadOnlyList<RezeptUebersicht>> AuflistenAsync(string? suche, CancellationToken cancellationToken = default);

    Task<Berechtigungen> BerechtigungenAsync(CancellationToken cancellationToken = default);

    /// <summary><c>null</c>, wenn es das Rezept nicht gibt.</summary>
    Task<MitStand<RezeptDetails>?> RezeptAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary><c>null</c>, wenn es das Rezept oder die Fassung nicht gibt.</summary>
    Task<MitStand<FassungDetails>?> FassungAsync(Guid id, int nummer, CancellationToken cancellationToken = default);

    Task<MitStand<RezeptDetails>> AnlegenAsync(string name, RezeptInhalt inhalt, CancellationToken cancellationToken = default);

    Task<SkaliertesRezept> UmrechnenAsync(Guid id, Umrechnung ziel, int? fassung, CancellationToken cancellationToken = default);

    Task<MitStand<FassungDetails>> EntwurfAnlegenAsync(Guid id, string stand, CancellationToken cancellationToken = default);

    Task<MitStand<FassungDetails>> EntwurfSpeichernAsync(Guid id, string stand, RezeptInhalt inhalt, CancellationToken cancellationToken = default);

    Task EntwurfVerwerfenAsync(Guid id, string stand, CancellationToken cancellationToken = default);

    Task<MitStand<FassungDetails>> FreigebenAsync(Guid id, string stand, CancellationToken cancellationToken = default);
}
