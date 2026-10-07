# GDShrapt.Converter

`GDShrapt.Converter` provides a .NET 10 batch-analysis and conversion-planning API. It is independent of Godot editor/plugin lifetimes and disables file watching and incremental parsing by default.

## Analyze a project

```csharp
using var analysis = await GDConversionAnalyzer.AnalyzeAsync(projectPath);

foreach (var script in analysis.Scripts)
{
    var syntaxTree = script.Class;
    var semanticModel = script.SemanticModel;
}
```

The analyzer finds the nearest parent directory containing `project.godot`, loads the complete project script set before semantic analysis, then optionally enriches call-site type information. This load-before-analyze order allows global classes and cross-file references to be resolved without depending on file enumeration order. The returned analysis owns the `GDScriptProject` and must be disposed.

Set `GDConversionAnalysisOptions.FocusPath` to a directory path to limit semantic analysis and call-site inference to scripts in that directory and its subdirectories. Relative focus paths are resolved from the project root. Other scripts remain loaded as project-wide type and name context.

## Plan syntax conversion

Implement `IGDConversionRule` for a specific syntax node or terminal token. `GDConversionService.CreatePlan` walks each script's complete syntax tree, applies the highest-priority matching rule to each syntax element, and records the mapped Roslyn C# syntax node or an explicit ignore reason. Elements without a matching rule remain `Unmapped`, and `GDConversionPlan.IsComplete` stays false until all syntax is handled. Registration order breaks priority ties.

Each rule receives a `GDConversionNodeContext` containing the syntax element, its parent, already evaluated `ChildMappings`, the script, and the semantic model. The context also provides flow-sensitive `GetVariableTypeAt` and `GetFlowStateAtLocation` queries, so focused rules can make type- and scope-aware decisions without reparsing the containing class. Rules can create Roslyn syntax using `Microsoft.CodeAnalysis.CSharp.SyntaxFactory` and compose their child mappings into larger expressions or statements.

`GDConversionNodeResult.Converted` can optionally receive a `GDConversionDestination`. It names the fully qualified destination type and can specify an `AssemblyName`, a relative path override, an `InnerClassName`, and an `Order`. Most member rules should omit the destination so their syntax contributes to the containing mapped type. A specified inner-class destination routes a declaration contribution into that nested C# type; a source inner class that needs its own Godot-visible type can instead map to a top-level declaration with its own destination name.

Rules do not calculate filesystem paths. The plan resolves a destination's relative-path override when present; otherwise it uses the source script's project-relative path with a `.cs` extension. Empty assembly names identify the default project assembly; assembly metadata stays on the planned file for higher-level routing. Destinations resolving to the same assembly and output path share one file. Contributions to the same fully qualified destination type are merged, and incompatible C# declaration kinds or names are rejected. `Order` sorts type contributions and explicitly routed members; ties preserve plan traversal order.

Type declarations automatically compose mapped GDScript class members, method mappings compose mapped statement blocks, and explicit destinations can route syntax into an outer or named inner class. The resulting files are exposed as `GDConversionPlan.Files`. Only a complete plan can be written; `WriteOutputs(plan, outputPath)` validates every destination before writing any files. Relative `outputPath` values are resolved from the directory containing `project.godot`. Output files may be written under that output directory, or elsewhere outside the Godot project directory but still within the nearest ancestor containing a `.sln` or `.slnx` file. Other locations inside the Godot project and paths outside the solution are rejected. If no solution file is found, the output directory must remain inside the Godot project and writes are restricted to that directory.

```csharp
var plan = service.CreatePlan(analysis, new GDConversionRuleSet(new[]
{
    new PatternMatchingRule(),
    new StandardVariableRule(),
    new ExplicitlyIgnoredSyntaxRule()
}));

if (!plan.IsComplete)
    throw new InvalidOperationException("Some syntax has no conversion decision.");

service.WriteOutputs(plan, outputPath);
```

No GDScript-to-C# translation rules are built in yet. Translation choices such as Godot API mapping, helper-library placement, and `#TOOLS` output require explicit rule implementations.

The CLI `analyze` command writes `gdshrapt-analysis.json`. `convert <project> <outputPath> [--dry-run]` analyzes the project, reports converted, ignored, and unmapped syntax-element counts, and writes the planned declaration files only when the plan is complete. A relative `outputPath` is interpreted from the Godot project directory; destination path overrides are relative to that output directory and may route files to sibling class libraries within the solution.