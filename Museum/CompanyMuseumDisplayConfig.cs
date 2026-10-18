using BepInEx.Configuration;

namespace LethalCards.Museum;

internal static class CompanyMuseumDisplayConfig
{
    private const string Section = "Company Museum Display";

    internal static float FallbackCenterX { get; private set; } = -27.88f;
    internal static float FallbackCenterZ { get; private set; } = -18.17f;
    internal static float ReferenceFloorY { get; private set; } = -2.68f;
    internal static float DesiredLength { get; private set; } = 5.10f;
    internal static float Height { get; private set; } = 3.40f;
    internal static float Depth { get; private set; } = 0.22f;
    internal static float PedestalClearance { get; private set; } = 0.20f;
    internal static float WallClearance { get; private set; } = 0.02f;
    internal static float RawVisualScale { get; private set; } = 1f;
    internal static float GradedVisualScale { get; private set; } = 1f;

    internal static void Load(ConfigFile config)
    {
        FallbackCenterX = BindMigratedFloat(
            config, "FallbackCenterX", -27.88f, -27.85f,
            "World X used only when live pedestal geometry cannot be measured.");
        FallbackCenterZ = BindMigratedFloat(
            config, "FallbackCenterZ", -18.17f, -18.18f,
            "World Z used only when live pedestal geometry cannot be measured.");
        ReferenceFloorY = BindMigratedFloat(
            config, "ReferenceFloorY", -2.68f, -2.63f,
            "Validated Company floor height used to select a nearby downward raycast hit.");
        DesiredLength = config.Bind(
            Section, "DesiredLength", 5.10f,
            "Authored museum main-body length used for placement and interaction bounds; the prefab is not rescaled.").Value;
        Height = config.Bind(
            Section, "Height", 3.40f,
            "Authored museum main-body height used for floor placement and interaction bounds.").Value;
        Depth = config.Bind(
            Section, "Depth", 0.22f,
            "Authored museum main-body depth used for interaction bounds.").Value;
        PedestalClearance = config.Bind(
            Section, "PedestalClearance", 0.20f,
            "Minimum desired gap between the museum main body and each grading pedestal's rendered bounds.").Value;
        WallClearance = config.Bind(
            Section, "WallClearance", 0.02f,
            "Small world-space gap maintained between the museum's structural back surface and the detected Company wall.").Value;
        RawVisualScale = config.Bind(
            Section, "RawVisualScale", 1f,
            "Scale multiplier for ungraded visual-only cards in museum slots.").Value;
        GradedVisualScale = config.Bind(
            Section, "GradedVisualScale", 1f,
            "Scale multiplier for graded visual-only slabs in museum slots.").Value;

        DesiredLength = UnityEngine.Mathf.Clamp(DesiredLength, 0.5f, 8f);
        Height = UnityEngine.Mathf.Clamp(Height, 0.5f, 6f);
        Depth = UnityEngine.Mathf.Clamp(Depth, 0.05f, 1f);
        PedestalClearance = UnityEngine.Mathf.Clamp(PedestalClearance, 0f, 1f);
        WallClearance = UnityEngine.Mathf.Clamp(WallClearance, 0.005f, 0.25f);
        RawVisualScale = UnityEngine.Mathf.Clamp(RawVisualScale, 0.25f, 2f);
        GradedVisualScale = UnityEngine.Mathf.Clamp(GradedVisualScale, 0.25f, 2f);

        config.Save();
        Plugin.Log.LogInfo(
            $"COMPANY MUSEUM CONFIG | DesiredLength={DesiredLength:F2} | " +
            $"Height={Height:F2} | Depth={Depth:F2} | " +
            $"PedestalClearance={PedestalClearance:F2} | WallClearance={WallClearance:F3} | " +
            $"RawVisualScale={RawVisualScale:F2} | GradedVisualScale={GradedVisualScale:F2}");
    }

    private static float BindMigratedFloat(
        ConfigFile config,
        string key,
        float currentDefault,
        float previousDefault,
        string description)
    {
        ConfigEntry<float> entry = config.Bind(
            Section, key, currentDefault, description);
        if (UnityEngine.Mathf.Abs(entry.Value - previousDefault) < 0.0001f)
        {
            entry.Value = currentDefault;
            Plugin.Log.LogInfo(
                $"COMPANY MUSEUM CONFIG MIGRATED | Key={key} | " +
                $"OldDefault={previousDefault:F2} | NewDefault={currentDefault:F2}");
        }
        return entry.Value;
    }
}
