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

## Add conversion rules

Implement `IGDConversionRule` to generate one or more relative-path outputs from an analyzed script. Rules are considered by descending priority; registration order breaks ties. `GDConversionService.CreatePlan` records all matching rules and the selected rule, and `WriteOutputs` verifies outputs remain under the destination directory.

No GDScript-to-C# translation rules are built in yet. Translation choices such as Godot API mapping, helper-library placement, and `#TOOLS` output require explicit rule implementations.

The CLI `analyze` command writes `gdshrapt-analysis.json`. `convert <project> <output> --dry-run` analyzes and reports the plan without writing files; without configured rules it reports that no outputs were produced.