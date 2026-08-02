using System.Linq;
using System.Text;
using BenchmarkDotNet.Attributes;
using GDShrapt.Reader;

namespace GDShrapt.Semantics.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for token position queries (StartLine / EndLine) on frozen and unfrozen trees.
/// Reading positions for a whole file is quadratic on an unfrozen tree, because every
/// NewLinesCount lookup rewalks the sibling subtrees. Freeze() fills the caches in one pass.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class TokenPositionBenchmarks
{
    [Params(200, 1000, 5000)]
    public int Lines { get; set; }

    private GDScriptReader _reader = null!;
    private string _source = "";
    private GDClassDeclaration _unfrozenTree = null!;
    private GDClassDeclaration _frozenTree = null!;

    [GlobalSetup]
    public void Setup()
    {
        _reader = new GDScriptReader();
        _source = GenerateFile(Lines);

        _unfrozenTree = _reader.ParseFileContent(_source);

        _frozenTree = _reader.ParseFileContent(_source);
        _frozenTree.Freeze();
    }

    private static string GenerateFile(int lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine("extends Node");
        sb.AppendLine();

        var methodCount = lines / 5;

        for (int i = 0; i < methodCount; i++)
        {
            sb.AppendLine($"func method_{i}(a, b):");
            sb.AppendLine($"\tvar local = a + b * {i}");
            sb.AppendLine("\tif local > 0:");
            sb.AppendLine($"\t\tlocal = local - {i}");
            sb.AppendLine("\treturn local");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [Benchmark(Baseline = true)]
    public int StartLine_AllTokens_Unfrozen()
    {
        var sum = 0;

        foreach (var token in _unfrozenTree.AllTokens)
            sum += token.StartLine;

        return sum;
    }

    [Benchmark]
    public int StartLine_AllTokens_Frozen()
    {
        var sum = 0;

        foreach (var token in _frozenTree.AllTokens)
            sum += token.StartLine;

        return sum;
    }

    [Benchmark]
    public int Range_AllIdentifiers_Frozen()
    {
        var sum = 0;

        foreach (var identifier in _frozenTree.AllTokens.OfType<GDIdentifier>())
            sum += identifier.StartLine + identifier.EndLine + identifier.StartColumn;

        return sum;
    }

    [Benchmark]
    public GDClassDeclaration Parse_Only()
    {
        return _reader.ParseFileContent(_source);
    }

    [Benchmark]
    public GDClassDeclaration Parse_And_Freeze()
    {
        var tree = _reader.ParseFileContent(_source);
        tree.Freeze();
        return tree;
    }
}
