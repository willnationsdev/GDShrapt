using GDShrapt.CLI.Core;
using System;
using System.Threading.Tasks;

namespace GDShrapt.Plugin;

/// <summary>
/// Formats the whole GDScript document via the shared core format handler.
/// </summary>
internal class FormatCodeCommand : Command
{
    public FormatCodeCommand(GDShraptPlugin plugin)
        : base(plugin)
    {
    }

    public override Task Execute(IScriptEditor controller)
    {
        Logger.Info("Format code requested");

        if (!controller.IsValid)
        {
            Logger.Info("Format code cancelled: Editor is not valid");
            return Task.CompletedTask;
        }

        var content = controller.Text;
        if (string.IsNullOrEmpty(content))
        {
            Logger.Info("Format code cancelled: No content to format");
            return Task.CompletedTask;
        }

        var format = Plugin.ServiceRegistry.GetService<IGDFormatHandler>();
        if (format == null)
        {
            Logger.Info("Format code cancelled: handler not available");
            return Task.CompletedTask;
        }

        try
        {
            var formatted = format.FormatCode(content, Plugin.ConfigManager?.Config?.Formatter);

            if (string.IsNullOrEmpty(formatted) || formatted == content)
            {
                Logger.Info("Code is already properly formatted");
                return Task.CompletedTask;
            }

            controller.Text = formatted;
            controller.ReloadScriptFromText();
            Logger.Info("Code formatted successfully");
        }
        catch (Exception ex)
        {
            Logger.Error($"Format failed: {ex.Message}");
            Logger.Error(ex);
        }

        return Task.CompletedTask;
    }
}
