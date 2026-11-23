using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Rezeptverwaltung.Rezeptur.Application.Ports;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.Rezeptur.Infrastructure;
using Rezeptverwaltung.SharedKernel;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Architecture = ArchUnitNET.Domain.Architecture;
using Assembly = System.Reflection.Assembly;

namespace Rezeptverwaltung.ArchitectureTests;

/// <summary>
/// Erzwingt die Architekturregeln aus ADR 0002 (modularer Monolith, hexagonale Module).
/// Neue Module werden in <see cref="Module"/> ergänzt.
/// </summary>
public sealed class ArchitekturTests
{
    private static readonly Assembly SharedKernel = typeof(Entity<>).Assembly;

    private static readonly (string Name, Assembly Domain, Assembly Application, Assembly Infrastructure)[] Module =
    [
        ("Rezeptur", typeof(Rezept).Assembly, typeof(IRezeptRepository).Assembly, typeof(RezepturModule).Assembly),
    ];

    private static readonly Architecture Architektur = new ArchLoader()
        .LoadAssemblies([SharedKernel, typeof(Program).Assembly, .. Module.SelectMany(m => new[] { m.Domain, m.Application, m.Infrastructure })])
        .Build();

    private const string Frameworks = @"^(Microsoft\.AspNetCore|Microsoft\.EntityFrameworkCore|Microsoft\.Extensions|Npgsql|System\.Net\.Http)(\..*)?$";

    public static TheoryData<string> ModulNamen => new(Module.Select(m => m.Name));

    [Theory]
    [MemberData(nameof(ModulNamen))]
    public void Domain_ist_frei_von_Frameworks_und_aeusseren_Schichten(string modul)
    {
        var m = Module.Single(x => x.Name == modul);

        Types().That().ResideInAssembly(m.Domain).Should()
            .NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(Frameworks))
            .AndShould().NotDependOnAny(Types().That().ResideInAssembly(m.Application))
            .AndShould().NotDependOnAny(Types().That().ResideInAssembly(m.Infrastructure))
            .Check(Architektur);
    }

    [Theory]
    [MemberData(nameof(ModulNamen))]
    public void Application_kennt_keine_Infrastruktur(string modul)
    {
        var m = Module.Single(x => x.Name == modul);

        Types().That().ResideInAssembly(m.Application).Should()
            .NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(Frameworks))
            .AndShould().NotDependOnAny(Types().That().ResideInAssembly(m.Infrastructure))
            .Check(Architektur);
    }

    [Fact]
    public void SharedKernel_kennt_keine_Module() =>
        Types().That().ResideInAssembly(SharedKernel).Should()
            .NotDependOnAny(Types().That().ResideInNamespaceMatching(@"^Rezeptverwaltung\.(?!SharedKernel).*"))
            .Check(Architektur);

    [Theory]
    [MemberData(nameof(ModulNamen))]
    public void Module_greifen_nicht_auf_andere_Module_zu(string modul)
    {
        var eigene = $@"^Rezeptverwaltung\.({modul}|SharedKernel)(\..*)?$";
        var fremde = Types().That().ResideInNamespaceMatching(@"^Rezeptverwaltung\..*")
            .And().DoNotResideInNamespaceMatching(eigene)
            .And().DoNotResideInNamespaceMatching(@"^Rezeptverwaltung\.Api(\..*)?$");

        Types().That().ResideInNamespaceMatching($@"^Rezeptverwaltung\.{modul}(\..*)?$").Should()
            .NotDependOnAny(fremde)
            .Check(Architektur);
    }

    [Theory]
    [MemberData(nameof(ModulNamen))]
    public void Infrastruktur_veroeffentlicht_nur_den_Modul_Einstiegspunkt(string modul)
    {
        var m = Module.Single(x => x.Name == modul);

        // Ausnahme: von EF Core generierte Migrationen sind zwingend öffentlich.
        Classes().That().ResideInAssembly(m.Infrastructure).And().ArePublic()
            .And().DoNotResideInNamespaceMatching(@"\.Persistence\.Migrations$").Should()
            .HaveNameEndingWith("Module")
            .Check(Architektur);
    }

    [Fact]
    public void Aggregate_und_Value_Objects_sind_versiegelt() =>
        Classes().That().AreAssignableTo(typeof(Entity<>)).And().AreNotAbstract().Should().BeSealed()
            .Check(Architektur);
}
