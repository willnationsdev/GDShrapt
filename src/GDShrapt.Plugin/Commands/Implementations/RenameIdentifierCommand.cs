using Godot;
using GDShrapt.CLI.Core;
using GDShrapt.Reader;
using GDShrapt.Semantics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GDShrapt.Plugin;

internal class RenameIdentifierCommand : Command
{
    RenamingDialog _renamingDialog;
    Action<string, int, int, int> _renamingDialogNavigateHandler;
    NodeRenamingDialog _nodeRenamingDialog;
    GDNodePathReferenceFinder _referenceFinder;

    public RenameIdentifierCommand(GDShraptPlugin plugin)
        : base(plugin)
    {
    }

    public override async Task Execute(IScriptEditor controller)
    {
        Logger.Info("Rename Identifier requested");

        if (!controller.IsValid)
        {
            Logger.Info($"Rename Identifier cancelled: Editor is not valid");
            return;
        }

        var line = controller.CursorLine;
        var column = controller.CursorColumn;

        var @class = controller.GetClass();
        if (@class == null)
        {
            Logger.Info("Renaming cancelled: no class declaration");
            return;
        }

        // Use GDPositionFinder for optimized token lookup (TryGetTokenByPosition with early exit)
        var finder = new GDPositionFinder(@class);
        var token = finder.FindTokenAtPosition(line, column);

        // Handle GDPathSpecifier for NodePath ($Player, $Root/Child)
        if (token is GDPathSpecifier pathSpec && pathSpec.Type == GDPathSpecifierType.Identifier)
        {
            var parentPathList = GDPositionFinder.FindParent<GDPathList>(token);
            if (parentPathList != null)
            {
                if (await RenamePathListNodeFromSpecifier(controller, parentPathList, pathSpec))
                {
                    controller.Text = @class.ToString();
                    return;
                }
            }
            Logger.Info("Renaming cancelled: could not find PathList for PathSpecifier");
            return;
        }

        var identifier = token as GDIdentifier;
        if (identifier == null)
        {
            Logger.Info("Renaming cancelled: no identifier");
            return;
        }

        Logger.Info($"Renaming identifier '{identifier}'");

        var parent = identifier.Parent;

        // Node-path identifier in a path list → scene-aware node rename (kept on GDRenameService).
        if (parent is GDPathList pathList)
        {
            if (await RenamePathListNode(controller, pathList, identifier))
                controller.Text = @class.ToString();
            return;
        }

        // Every other symbol (method/var/signal/enum/member/parameter/inner class/...) goes through
        // the shared core rename handler — cross-file, strict-only, identical to CLI/LSP.
        await RenameSymbol(controller, identifier.Sequence);
    }

    private async Task RenameSymbol(IScriptEditor controller, string oldName)
    {
        var renameParams = await AskParametersAndPrepareIdentifier(oldName, null);
        if (renameParams == null)
            return;

        var newName = renameParams.NewName;
        var filePath = controller.ScriptFile?.FullPath ?? controller.ScriptPath;
        if (string.IsNullOrEmpty(filePath))
            return;

        var rename = Plugin.ServiceRegistry.GetService<IGDRenameHandler>();
        if (rename == null)
        {
            Logger.Info("Rename cancelled: handler not available");
            return;
        }

        var result = rename.Plan(oldName, newName, filePath);
        if (!result.Success || result.StrictEdits.Count == 0)
        {
            Logger.Info($"Rename: no type-verified references for '{oldName}'");
            return;
        }

        ApplyRenameEdits(controller, filePath, result, rename);
    }

    private void ApplyRenameEdits(IScriptEditor controller, string currentFilePath, GDRenameResult result, IGDRenameHandler rename)
    {
        var currentEdits = result.StrictEdits.Where(e => PathEquals(e.FilePath, currentFilePath)).ToList();
        var otherEdits = result.StrictEdits.Where(e => !PathEquals(e.FilePath, currentFilePath)).ToList();

        // Current file: apply to the open editor buffer (atomic undo, keeps the tab live).
        if (currentEdits.Count > 0)
        {
            controller.Text = ApplyEditsToText(controller.Text, currentEdits);
            controller.ReloadScriptFromText();
        }

        // Other files: written on disk; the project file-watcher reloads + re-analyzes them.
        if (otherEdits.Count > 0)
            rename.ApplyEdits(otherEdits);

        Logger.Info($"Renamed {result.StrictEdits.Count} reference(s) across {result.FileCount} file(s)");
        if (result.PotentialEdits.Count > 0)
            Logger.Info($"{result.PotentialEdits.Count} duck-typed reference(s) found but not applied (lower confidence)");
    }

    private static string ApplyEditsToText(string text, List<GDTextEdit> edits)
    {
        var lines = text.Split('\n');
        foreach (var e in edits.OrderByDescending(x => x.Line).ThenByDescending(x => x.Column))
        {
            var line0 = GDPluginPositionAdapter.FromLine(e.Line);
            var col0 = GDPluginPositionAdapter.FromEditColumn(e.Column);
            if (line0 < 0 || line0 >= lines.Length)
                continue;

            var l = lines[line0];
            var oldLen = e.OldText?.Length ?? 0;
            if (col0 < 0 || col0 + oldLen > l.Length)
                continue;
            if (!string.IsNullOrEmpty(e.OldText) && l.Substring(col0, oldLen) != e.OldText)
                continue;

            lines[line0] = l.Substring(0, col0) + e.NewText + l.Substring(col0 + oldLen);
        }
        return string.Join("\n", lines);
    }

    private static bool PathEquals(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        return string.Equals(
            System.IO.Path.GetFullPath(a).Replace('\\', '/'),
            System.IO.Path.GetFullPath(b).Replace('\\', '/'),
            System.StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> RenamePathListNode(IScriptEditor scriptEditor, GDPathList pathList, GDIdentifier identifier)
    {
        Logger.Info("Renaming Path List Node");

        // Find the GDPathSpecifier that contains this identifier
        var pathSpecifier = FindPathSpecifier(pathList, identifier);
        if (pathSpecifier == null)
        {
            Logger.Info("Could not find path specifier for identifier");
            return false;
        }

        if (pathSpecifier.Type != GDPathSpecifierType.Identifier)
        {
            Logger.Info("Cannot rename . or .. path specifiers");
            return false;
        }

        var nodeName = pathSpecifier.IdentifierValue;
        Logger.Info($"Renaming node path: {nodeName}");

        // Initialize reference finder if needed
        if (_referenceFinder == null)
        {
            _referenceFinder = new GDNodePathReferenceFinder(Map);
        }

        // Get the current script
        var ScriptFile = scriptEditor?.ScriptFile;
        if (ScriptFile == null)
        {
            Logger.Info("Current script not found");
            return false;
        }

        // Find scenes that use this script
        var scenes = _referenceFinder.GetScenesForScript(ScriptFile).ToList();

        // Collect all references
        var allReferences = new List<GDNodePathReference>();

        // Add GDScript references
        allReferences.AddRange(_referenceFinder.FindGDScriptReferences(nodeName));

        // Add scene references for each scene
        foreach (var scenePath in scenes)
        {
            allReferences.AddRange(_referenceFinder.FindSceneReferences(scenePath, nodeName));
        }

        if (allReferences.Count == 0)
        {
            Logger.Info("No references found for this node path");
            // Just rename the local reference
            return await RenameLocalPathSpecifier(pathSpecifier, nodeName);
        }

        // Show dialog
        if (_nodeRenamingDialog == null)
        {
            Editor.AddChild(_nodeRenamingDialog = new NodeRenamingDialog());
        }

        // Set warning if script is used in multiple scenes
        if (scenes.Count > 1)
        {
            _nodeRenamingDialog.SetWarning($"Note: This script is used in {scenes.Count} scenes.");
        }
        else if (scenes.Count == 0)
        {
            _nodeRenamingDialog.SetWarning("Note: Could not find scene for this script. Only GDScript references will be renamed.");
        }
        else
        {
            _nodeRenamingDialog.SetWarning(null);
        }

        var parameters = await _nodeRenamingDialog.ShowForResult(nodeName, allReferences);
        if (parameters == null || string.IsNullOrWhiteSpace(parameters.NewName))
        {
            Logger.Info("Node renaming cancelled");
            return false;
        }

        var newName = InternalMethods.PrepareIdentifier(parameters.NewName, "");

        // Plan rename using GDRenameService
        var renameService = new GDRenameService(Map);
        var renamePlan = renameService.PlanNodePathRename(nodeName, newName);

        if (!renamePlan.Success)
        {
            Logger.Error($"Node rename failed: {renamePlan.ErrorMessage}");
            return false;
        }

        // Apply edits
        foreach (var edit in renamePlan.StrictEdits)
        {
            renameService.ApplyEditsToFile(edit.FilePath, new[] { edit });
        }

        // Clear scene cache to reload updated scenes
        Map.SceneTypesProvider?.ClearCache();

        Logger.Info($"Node path renamed from '{nodeName}' to '{newName}'");
        return true;
    }

    private async Task<bool> RenamePathListNodeFromSpecifier(
        IScriptEditor scriptEditor,
        GDPathList pathList,
        GDPathSpecifier pathSpecifier)
    {
        Logger.Info("Renaming Path List Node from PathSpecifier");

        if (pathSpecifier.Type != GDPathSpecifierType.Identifier)
        {
            Logger.Info("Cannot rename . or .. path specifiers");
            return false;
        }

        var nodeName = pathSpecifier.IdentifierValue;
        Logger.Info($"Renaming node path: {nodeName}");

        // Initialize reference finder if needed
        if (_referenceFinder == null)
        {
            _referenceFinder = new GDNodePathReferenceFinder(Map);
        }

        // Get the current script
        var ScriptFile = scriptEditor?.ScriptFile;
        if (ScriptFile == null)
        {
            Logger.Info("Current script not found");
            return false;
        }

        // Find scenes that use this script
        var scenes = _referenceFinder.GetScenesForScript(ScriptFile).ToList();

        // Collect all references
        var allReferences = new List<GDNodePathReference>();

        // Add GDScript references
        allReferences.AddRange(_referenceFinder.FindGDScriptReferences(nodeName));

        // Add scene references for each scene
        foreach (var scenePath in scenes)
        {
            allReferences.AddRange(_referenceFinder.FindSceneReferences(scenePath, nodeName));
        }

        if (allReferences.Count == 0)
        {
            Logger.Info("No references found for this node path");
            // Just rename the local reference
            var parameters = await AskParametersAndPrepareIdentifier(nodeName);
            if (parameters == null)
                return false;
            pathSpecifier.IdentifierValue = parameters.NewName;
            return true;
        }

        // Show dialog
        if (_nodeRenamingDialog == null)
        {
            Editor.AddChild(_nodeRenamingDialog = new NodeRenamingDialog());
        }

        // Set warning if script is used in multiple scenes
        if (scenes.Count > 1)
        {
            _nodeRenamingDialog.SetWarning($"Note: This script is used in {scenes.Count} scenes.");
        }
        else if (scenes.Count == 0)
        {
            _nodeRenamingDialog.SetWarning("Note: Could not find scene for this script. Only GDScript references will be renamed.");
        }
        else
        {
            _nodeRenamingDialog.SetWarning(null);
        }

        var dialogParams = await _nodeRenamingDialog.ShowForResult(nodeName, allReferences);
        if (dialogParams == null || string.IsNullOrWhiteSpace(dialogParams.NewName))
        {
            Logger.Info("Node renaming cancelled");
            return false;
        }

        var newName = InternalMethods.PrepareIdentifier(dialogParams.NewName, "");

        // Plan rename using GDRenameService
        var renameService = new GDRenameService(Map);
        var renamePlan = renameService.PlanNodePathRename(nodeName, newName);

        if (!renamePlan.Success)
        {
            Logger.Error($"Node rename failed: {renamePlan.ErrorMessage}");
            return false;
        }

        // Apply edits
        foreach (var edit in renamePlan.StrictEdits)
        {
            renameService.ApplyEditsToFile(edit.FilePath, new[] { edit });
        }

        // Clear scene cache to reload updated scenes
        Map.SceneTypesProvider?.ClearCache();

        Logger.Info($"Node path renamed from '{nodeName}' to '{newName}'");
        return true;
    }

    private async Task<bool> RenameLocalPathSpecifier(GDPathSpecifier pathSpecifier, string currentName)
    {
        // Simple case: just rename the local reference
        var parameters = await AskParametersAndPrepareIdentifier(currentName);
        if (parameters == null)
            return false;

        pathSpecifier.IdentifierValue = parameters.NewName;
        return true;
    }

    private GDPathSpecifier FindPathSpecifier(GDPathList pathList, GDIdentifier identifier)
    {
        // The identifier should be somewhere in the path list
        // We need to find the corresponding GDPathSpecifier
        var identifierSequence = identifier.Sequence;

        foreach (var layer in pathList.OfType<GDLayersList>())
        {
            foreach (var specifier in layer.OfType<GDPathSpecifier>())
            {
                if (specifier.Type == GDPathSpecifierType.Identifier &&
                    specifier.IdentifierValue == identifierSequence)
                {
                    // Check if positions match
                    if (specifier.StartLine == identifier.StartLine &&
                        specifier.StartColumn == identifier.StartColumn)
                    {
                        return specifier;
                    }

                    // Also check if this is the only specifier with this name at this line
                    var sameLineSpecifiers = pathList
                        .OfType<GDLayersList>()
                        .SelectMany(l => l.OfType<GDPathSpecifier>())
                        .Where(s => s.Type == GDPathSpecifierType.Identifier &&
                                    s.IdentifierValue == identifierSequence &&
                                    s.StartLine == identifier.StartLine)
                        .ToList();

                    if (sameLineSpecifiers.Count == 1)
                    {
                        return sameLineSpecifiers[0];
                    }
                }
            }
        }

        // Fallback: return first matching specifier
        foreach (var layer in pathList.OfType<GDLayersList>())
        {
            foreach (var specifier in layer.OfType<GDPathSpecifier>())
            {
                if (specifier.Type == GDPathSpecifierType.Identifier &&
                    specifier.IdentifierValue == identifierSequence)
                {
                    return specifier;
                }
            }
        }

        return null;
    }

    private async Task<RenamingParameters?> AskParametersAndPrepareIdentifier(string sequence, LinkedList<GDMemberReference>? references = null)
    {
        var parameters = await AskRenamingParameters(sequence, references);

        if (parameters == null)
        {
            Logger.Info("Renaming cancelled - dialog returned null");
            return null;
        }

        var name = sequence;
        var newName = parameters.NewName;

        if (string.IsNullOrWhiteSpace(newName) || string.Equals(name, newName, System.StringComparison.Ordinal))
        {
            Logger.Info("Renaming cancelled - empty or same name");
            return null;
        }

        parameters.NewName = InternalMethods.PrepareIdentifier(newName, "");
        return parameters;
    }

    private async Task<RenamingParameters> AskRenamingParameters(string currentName, LinkedList<GDMemberReference>? references = null)
    {
        var scriptEditor = Editor;

        // Recreate dialog if it was freed or not in tree
        if (_renamingDialog == null || !GodotObject.IsInstanceValid(_renamingDialog) || !_renamingDialog.IsInsideTree())
        {
            if (_renamingDialog != null)
            {
                _renamingDialog.NavigateToReference -= _renamingDialogNavigateHandler;
                _renamingDialog.QueueFree();
            }
            _renamingDialog = new RenamingDialog();
            _renamingDialogNavigateHandler = OnNavigateToReference;
            _renamingDialog.NavigateToReference += _renamingDialogNavigateHandler;
            scriptEditor.AddChild(_renamingDialog);
        }

        _renamingDialog.Position = new Vector2I((int)(scriptEditor.Position.X + scriptEditor.Size.X / 2), (int)(scriptEditor.Position.Y + scriptEditor.Size.Y / 2));
        _renamingDialog.SetCurrentName(currentName);
        _renamingDialog.SetReferencesList(references);

        return await _renamingDialog.ShowForResult();
    }

    private void OnNavigateToReference(string filePath, int line, int column, int endColumn)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        // Convert to res:// path if needed
        var resourcePath = filePath;
        if (!resourcePath.StartsWith("res://"))
        {
            var projectPath = ProjectSettings.GlobalizePath("res://");
            if (filePath.StartsWith(projectPath))
                resourcePath = "res://" + filePath.Substring(projectPath.Length).Replace("\\", "/");
        }

        // Open the script and navigate to the line
        EditorInterface.Singleton.EditScript(Godot.GD.Load<Script>(resourcePath), line + 1, column);

        // Try to select the token if we have valid column range
        // Note: Selection will be handled by the active tab controller after EditScript
    }
}
