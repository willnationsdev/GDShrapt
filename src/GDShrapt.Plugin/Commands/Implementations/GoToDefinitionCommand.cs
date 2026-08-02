using GDShrapt.CLI.Core;
using System;
using System.Threading.Tasks;

namespace GDShrapt.Plugin;

internal class GoToDefinitionCommand : Command
{
    public GoToDefinitionCommand(GDShraptPlugin plugin)
        : base(plugin)
    {
    }

    public override Task Execute(IScriptEditor scriptEditor)
    {
        var line = scriptEditor.CursorLine;     // 0-based
        var column = scriptEditor.CursorColumn; // 0-based

        Logger.Info($"GoToDefinition requested {{{line}, {column}}}");

        var filePath = scriptEditor.ScriptFile?.FullPath ?? scriptEditor.ScriptPath;
        if (string.IsNullOrEmpty(filePath))
        {
            scriptEditor.RequestGodotLookup();
            return Task.CompletedTask;
        }

        var goToDef = Plugin.ServiceRegistry.GetService<IGDGoToDefHandler>();
        if (goToDef == null)
        {
            scriptEditor.RequestGodotLookup();
            return Task.CompletedTask;
        }

        // Delegate resolution to the shared core handler (same path as CLI/LSP): handles locals,
        // class members, cross-file types/members, and built-ins.
        var def = goToDef.FindDefinition(
            filePath,
            GDPluginPositionAdapter.ToHandlerLine(line),
            GDPluginPositionAdapter.ToHandlerColumn(column));

        // Built-in / node-path / resource / unresolved → defer to Godot's own lookup.
        if (def == null || def.IsInfoOnly || string.IsNullOrEmpty(def.FilePath))
        {
            Logger.Info("GoToDefinition: no navigable location, delegating to Godot");
            scriptEditor.RequestGodotLookup();
            return Task.CompletedTask;
        }

        var nameLen = def.SymbolName?.Length ?? 0;
        var targetLine = GDPluginPositionAdapter.FromLine(def.Line);             // 1-based → 0-based
        var targetCol = GDPluginPositionAdapter.FromDefinitionColumn(def.Column); // already 0-based

        if (string.Equals(def.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            scriptEditor.Select(targetLine, targetCol, targetLine, targetCol + nameLen);
            Logger.Info("GoToDefinition completed (same file)");
        }
        else
        {
            var targetScript = Map.GetScript(def.FilePath);
            var tab = targetScript != null ? Plugin.OpenScript(targetScript) : null;
            tab?.Editor?.Select(targetLine, targetCol, targetLine, targetCol + nameLen);
            Logger.Info("GoToDefinition completed (cross-file)");
        }

        return Task.CompletedTask;
    }
}
