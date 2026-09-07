using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LethalCards.Debugging;

[HarmonyPatch(typeof(PlayerControllerB), "Update")]
public static class PlayerPositionDebugPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        PlayerControllerB __instance)
    {
        if (__instance == null)
            return;

        if (
            GameNetworkManager.Instance == null ||
            __instance !=
                GameNetworkManager.Instance.localPlayerController)
        {
            return;
        }

        Keyboard keyboard =
            Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard.pKey.wasPressedThisFrame)
        {
            Vector3 position =
                __instance.transform.position;

            Vector3 forward =
                __instance.transform.forward;

            Plugin.Log.LogInfo(
                $"PLAYER POSITION MARKER | " +
                $"Position=({position.x:F2}, {position.y:F2}, {position.z:F2}) | " +
                $"Forward=({forward.x:F2}, {forward.y:F2}, {forward.z:F2})"
            );
        }
    }
}