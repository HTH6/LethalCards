using BepInEx.Logging;

namespace LethalCards;

/// <summary>
/// Keeps routine development diagnostics available without filling normal
/// production logs. Warnings and errors are never filtered.
/// </summary>
internal sealed class ModLog
{
    private readonly ManualLogSource source;

    internal ModLog(ManualLogSource source)
    {
        this.source = source;
    }

    internal void LogInfo(object data)
    {
        if (Plugin.EnableDebugLogging)
            source.LogInfo(data);
    }

    internal void LogDebug(object data)
    {
        if (Plugin.EnableDebugLogging)
            source.LogInfo(data);
    }

    internal void LogAlways(object data) => source.LogInfo(data);

    internal void LogWarning(object data) => source.LogWarning(data);

    internal void LogError(object data) => source.LogError(data);
}
