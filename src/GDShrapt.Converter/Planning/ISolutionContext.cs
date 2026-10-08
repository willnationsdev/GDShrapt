namespace GDShrapt.Converter.Planning;

public interface ISolutionContext
{
    /// <summary>
    /// The formatter associated with the main Godot project.
    /// </summary>
    IGDConversionFormatter DefaultFormatter { get; }

    /// <summary>
    /// The formatter associated with a specific assembly known to the .NET solution.
    /// </summary>
    /// <param name="assemblyName"></param>
    /// <returns></returns>
    IGDConversionFormatter Formatter(string assemblyName);

    /// <summary>
    /// The main Godot project context.
    /// </summary>
    ICsProjectContext DefaultProject { get; }

    /// <summary>
    /// The project context associated with a specific assembly known to the .NET solution.
    /// </summary>
    /// <param name="assemblyName"></param>
    /// <returns></returns>
    ICsProjectContext Project(string assemblyName);
}
