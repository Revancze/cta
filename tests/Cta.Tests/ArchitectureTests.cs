using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Cta.Tests.Storage;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Cta.Tests;

public class ArchitectureTests
{
    private static readonly Architecture Model = new ArchLoader()
        .LoadAssemblies(
            typeof(Cta.Contracts.AssemblyMarker).Assembly,
            typeof(Cta.Core.AssemblyMarker).Assembly,
            typeof(Cta.Storage.AssemblyMarker).Assembly,
            typeof(Cta.Cli.AssemblyMarker).Assembly
        )
        .Build();

    private static readonly IObjectProvider<IType> Contracts = Types()
        .That()
        .ResideInAssembly(typeof(Cta.Contracts.AssemblyMarker).Assembly)
        .As("Cta.Contracts");

    private static readonly IObjectProvider<IType> Core = Types()
        .That()
        .ResideInAssembly(typeof(Cta.Core.AssemblyMarker).Assembly)
        .As("Cta.Core");

    private static readonly IObjectProvider<IType> Storage = Types()
        .That()
        .ResideInAssembly(typeof(Cta.Storage.AssemblyMarker).Assembly)
        .As("Cta.Storage");

    private static readonly IObjectProvider<IType> Cli = Types()
        .That()
        .ResideInAssembly(typeof(Cta.Cli.AssemblyMarker).Assembly)
        .As("Cta.Cli");

    // @relation(CTA-ARCH-001, scope=range_start)
    [Fact]
    public void Contracts_do_not_depend_on_other_projects()
    {
        Types()
            .That()
            .Are(Contracts)
            .Should()
            .NotDependOnAny(Types().That().Are(Core).Or().Are(Storage).Or().Are(Cli))
            .Check(Model);
    }

    // @relation(CTA-ARCH-001, scope=range_end)

    // @relation(CTA-ARCH-002, scope=range_start)
    [Fact]
    public void Core_does_not_depend_on_storage_or_cli()
    {
        Types()
            .That()
            .Are(Core)
            .Should()
            .NotDependOnAny(Types().That().Are(Storage).Or().Are(Cli))
            .Check(Model);
    }

    // @relation(CTA-ARCH-002, scope=range_end)

    // @relation(CTA-ARCH-003, scope=range_start)
    [Fact]
    public void Storage_does_not_depend_on_cli()
    {
        Types().That().Are(Storage).Should().NotDependOnAny(Cli).Check(Model);
    }
    // @relation(CTA-ARCH-003, scope=range_end)
}
