using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;

namespace Rezeptverwaltung.Rezeptur.Infrastructure.Persistence;

/// <summary>
/// Abbildung des Aggregats <see cref="Rezept"/> auf genau einen Datensatz (ADR 0011):
/// Die Zutatenliste liegt als JSONB-Array im Rezept, weil Arrays die Reihenfolge erhalten
/// und die Reihenfolge der Zutaten fachlich bedeutsam ist.
/// </summary>
internal sealed class RezeptKonfiguration : IEntityTypeConfiguration<Rezept>
{
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

        builder.Ignore(r => r.DomainEvents);
        builder.Ignore(r => r.Gesamtgewicht);

        builder.OwnsMany(r => r.Zutaten, zutaten =>
        {
            zutaten.ToJson("zutaten");
            zutaten.Property(z => z.Bezeichnung).HasJsonPropertyName("bezeichnung");
            zutaten.Property(z => z.Menge)
                .HasJsonPropertyName("gramm")
                .HasConversion(gewicht => gewicht.Gramm, gramm => Gewicht.Erfasst(gramm));
        });
        builder.Navigation(r => r.Zutaten).HasField("_zutaten").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
