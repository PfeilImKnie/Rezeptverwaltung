namespace Rezeptverwaltung.Rezeptur.Domain.Tests;

/// <summary>Feste Zeit für reproduzierbare Tests.</summary>
internal static class Testzeit
{
    public static readonly DateTimeOffset Jetzt = new(2026, 11, 23, 9, 30, 0, TimeSpan.Zero);

    public static TimeProvider Anbieter { get; } = new FesteZeit(Jetzt);

    private sealed class FesteZeit(DateTimeOffset zeitpunkt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => zeitpunkt;
    }
}
