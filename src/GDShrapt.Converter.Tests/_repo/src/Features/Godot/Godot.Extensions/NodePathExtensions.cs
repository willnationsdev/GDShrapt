namespace Godot.Extensions;

public static class NodePathExtensions
{
    /// <summary>
    /// Returns the last element of a node path.
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static StringName GetNodeName(this NodePath path) => path.GetName(path.GetNameCount() - 1);
}
