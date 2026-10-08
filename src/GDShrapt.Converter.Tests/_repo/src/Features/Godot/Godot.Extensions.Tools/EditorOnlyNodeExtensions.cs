#if TOOLS
namespace Godot.Extensions;

public static class EditorOnlyNodeExtensions
{
    extension (Node? node)
    {
        public bool IsNodeInEditedScene
        {
            get
            {
                if (!Engine.IsEditorHint() || node is null || !GodotObject.IsInstanceValid(node)) return false;
                var root = EditorInterface.Singleton?.GetEditedSceneRoot();
                return root is not null && GodotObject.IsInstanceValid(root) && (root == node || root.IsAncestorOf(node));
            }
        }
    }
}

#endif
