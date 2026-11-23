using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

/// <summary>
/// Abbildung des Aggregats <see cref="Rezept"/> auf genau einen Datensatz (ADR 0011, ADR 0013):
/// Die Fassungen liegen als JSONB-Array im Rezept, jede mit ihrer Zutatenliste als JSON-Array.
/// Arrays erhalten die Reihenfolge; die Feldnamen sind ein Vertrag mit künftigen Migrationen.
/// </summary>
internal sealed class RezeptKonfiguration : IEntityTypeConfiguration<Rezept>
{
    /// <summary>Schatteneigenschaft für den Stand (PostgreSQL-Systemspalte xmin), Grundlage für ETag/If-Match.</summary>
    public const string Stand = "Version";

    /// <summary>
    /// Zeitpunkte im JSON verlustfrei im Roundtrip-Format („O“, 100-ns-Ticks). Ohne diese Umwandlung kürzt die
    /// Speicherung auf Mikrosekunden, und ein Client sähe in Antwort und späterem Abruf verschiedene Zeitpunkte.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, string> Zeitpunkt = new(
        zeitpunkt => zeitpunkt.ToString("O", CultureInfo.InvariantCulture),
        text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public void Configure(EntityTypeBuilder<Rezept> builder)
    {
        builder.ToTable("rezepte");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Wert, wert => new RezeptId(wert))
            .ValueGeneratedNever();

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasConversion(name => name.Wert, wert => RezeptName.Von(wert))
            .IsRequired();

        builder.Property(r => r.LetzteFassungsnummer).HasColumnName("letzte_fassungsnummer");

        // Optimistische Nebenläufigkeit über die PostgreSQL-Systemspalte xmin (keine eigene Spalte nötig).
        builder.Property<uint>(Stand).IsRowVersion();

        builder.Ignore(r => r.DomainEvents);
        builder.Ignore(r => r.Zutaten);
        builder.Ignore(r => r.Gesamtgewicht);
        builder.Ignore(r => r.Entwurf);
        builder.Ignore(r => r.AktuelleFassung);
        builder.Ignore(r => r.MassgeblicheFassung);

        builder.OwnsMany(r => r.Fassungen, fassung =>
        {
            fassung.ToJson("fassungen");
            fassung.Property(f => f.Nummer).HasJsonPropertyName("nummer");
            fassung.Property(f => f.Status).HasJsonPropertyName("status").HasConversion<string>();
            fassung.Property(f => f.ErstelltAm).HasJsonPropertyName("erstelltAm").HasConversion(Zeitpunkt);
            fassung.Property(f => f.FreigegebenAm).HasJsonPropertyName("freigegebenAm").HasConversion(Zeitpunkt);
            fassung.Ignore(f => f.IstEntwurf);
            fassung.Ignore(f => f.Gesamtgewicht);

            fassung.OwnsMany(f => f.Zutaten, zutaten =>
            {
                // Ohne explizite Angabe schriebe EF den Navigationsnamen ("Zutaten"); alle Feldnamen sind klein (ADR 0011).
                zutaten.HasJsonPropertyName("zutaten");
                zutaten.Property(z => z.Bezeichnung).HasJsonPropertyName("bezeichnung");
                zutaten.Property(z => z.Menge)
                    .HasJsonPropertyName("gramm")
                    .HasConversion(gewicht => gewicht.Gramm, gramm => Gewicht.Erfasst(gramm));
            });
            fassung.Navigation(f => f.Zutaten).HasField("_zutaten").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Navigation(r => r.Fassungen).HasField("_fassungen").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
