using BepInEx.Configuration;
using UnityEngine;

namespace LethalCards.ShipDisplay;

internal static class ShipCardDisplayConfig
{
    private const string Section = "Ship Card Display";
    // This is the cabinet root offset. The corresponding placement-proxy point
    // is approximately (-0.76, 2.44, -4.59) because the bounds center is 0.01
    // along local Z and the default cabinet rotation is 180 degrees.
    private static readonly Vector3 DefaultLocalPosition = new(-0.76f, 2.44f, -4.58f);
    private static readonly Vector3 DefaultLocalEulerAngles = new(0f, 180f, 0f);
    private static readonly Vector3[] PreviousDefaultLocalPositions =
    {
        new(-0.87f, 1.10f, -5.11f),
        new(-0.87f, 1.45f, -5.11f)
    };

    internal static Vector3 LocalPosition { get; private set; } = Vector3.zero;
    internal static Vector3 LocalEulerAngles { get; private set; } = Vector3.zero;

    internal static void Load(ConfigFile config)
    {
        ConfigEntry<float> positionX = BindEntry(
            config,
            "PositionX",
            DefaultLocalPosition.x,
            "Initial ship-relative X position used only when this save has no authoritative furniture placement. Press P in the ship to log ship-relative coordinates and camera surface hits.");
        ConfigEntry<float> positionY = BindEntry(
            config,
            "PositionY",
            DefaultLocalPosition.y,
            "Initial ship-relative Y position used only when this save has no authoritative furniture placement. Existing furniture saves are never overridden.");
        ConfigEntry<float> positionZ = BindEntry(
            config,
            "PositionZ",
            DefaultLocalPosition.z,
            "Initial ship-relative Z position used only when this save has no authoritative furniture placement. Press P in the ship to log ship-relative coordinates and camera surface hits.");
        ConfigEntry<float> rotationX = BindEntry(
            config,
            "RotationX",
            DefaultLocalEulerAngles.x,
            "Initial ship-relative X rotation in degrees, used only before the cabinet has a saved furniture placement.");
        ConfigEntry<float> rotationY = BindEntry(
            config,
            "RotationY",
            DefaultLocalEulerAngles.y,
            "Initial ship-relative Y rotation in degrees, used only before the cabinet has a saved furniture placement.");
        ConfigEntry<float> rotationZ = BindEntry(
            config,
            "RotationZ",
            DefaultLocalEulerAngles.z,
            "Initial ship-relative Z rotation in degrees, used only before the cabinet has a saved furniture placement.");

        Vector3 configuredPosition = new(positionX.Value, positionY.Value, positionZ.Value);
        Vector3 configuredRotation = new(rotationX.Value, rotationY.Value, rotationZ.Value);
        if (IsRecognizedPreviousDefault(configuredPosition, configuredRotation))
        {
            Vector3 previousConfiguredPosition = configuredPosition;
            positionX.Value = DefaultLocalPosition.x;
            positionY.Value = DefaultLocalPosition.y;
            positionZ.Value = DefaultLocalPosition.z;
            configuredPosition = DefaultLocalPosition;
            Plugin.Log.LogInfo(
                $"SHIP CARD DISPLAY CONFIG MIGRATED | " +
                $"PreviousLocalPosition={previousConfiguredPosition} | " +
                $"NewLocalPosition={DefaultLocalPosition}");
        }

        LocalPosition = configuredPosition;
        LocalEulerAngles = configuredRotation;

        config.Save();
        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY CONFIG | LocalPosition={LocalPosition} | " +
            $"LocalRotation={LocalEulerAngles}");
    }

    private static bool IsRecognizedPreviousDefault(
        Vector3 position,
        Vector3 rotation)
    {
        if (!Approximately(rotation, DefaultLocalEulerAngles))
            return false;

        foreach (Vector3 previousPosition in PreviousDefaultLocalPositions)
        {
            if (Approximately(position, previousPosition))
                return true;
        }

        return false;
    }

    private static bool Approximately(Vector3 left, Vector3 right) =>
        Mathf.Approximately(left.x, right.x) &&
        Mathf.Approximately(left.y, right.y) &&
        Mathf.Approximately(left.z, right.z);

    private static ConfigEntry<float> BindEntry(
        ConfigFile config,
        string key,
        float defaultValue,
        string description) =>
        config.Bind(Section, key, defaultValue, new ConfigDescription(description));
}
