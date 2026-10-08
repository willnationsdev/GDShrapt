using GDShrapt.Converter.Planning;

namespace GDShrapt.Converter.Tests;

internal sealed class TestSolutionContext : ISolutionContext
{
    public static TestSolutionContext Instance { get; } = new(false);

    private TestSolutionContext(bool supportsDoublePrecision)
    {
        DefaultProject = new TestCsProjectContext(supportsDoublePrecision);
        DefaultFormatter = new DefaultGDConversionFormatter(new GDConversionFormattingOptions());
    }

    public static TestSolutionContext WithDoublePrecision(bool supportsDoublePrecision)
        => new(supportsDoublePrecision);

    public IGDConversionFormatter DefaultFormatter { get; }
    public ICsProjectContext DefaultProject { get; }

    public IGDConversionFormatter Formatter(string assemblyName) => DefaultFormatter;

    public ICsProjectContext Project(string assemblyName) => DefaultProject;

    private sealed class TestCsProjectContext(bool supportsDoublePrecision) : ICsProjectContext
    {
        public string? ProjectFilePath => null;
        public string Configuration => "Testing";
        public string AssemblyName => "Tests";
        public string RootNamespace => "Tests";
        public string? TargetFramework => "net10.0";
        public string LanguageVersion => "latest";
        public bool NullableEnabled => true;
        public bool ImplicitUsingsEnabled => true;
        public bool SupportsDoublePrecision { get; } = supportsDoublePrecision;
        public IReadOnlySet<string> DefineConstants { get; } = new HashSet<string>();
        public IReadOnlySet<string> GlobalUsings { get; } = new HashSet<string>();
        public IReadOnlyDictionary<string, string> GlobalTypeAliases { get; } =
            new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> EvaluatedProperties { get; } =
            new Dictionary<string, string>();
    }
}
