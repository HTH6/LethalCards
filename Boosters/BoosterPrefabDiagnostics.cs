using System;
using UnityEngine;
using Unity.Netcode;
using LethalCards.Networking;

namespace LethalCards.Boosters;

internal static class BoosterPrefabDiagnostics
{
    internal static void ValidateAndLog(GameObject prefab, BoosterType expected, string stage)
    {
        GrabbableObject[] grabbables = prefab.GetComponents<GrabbableObject>();
        BoosterPackBehaviour pack = prefab.GetComponent<BoosterPackBehaviour>();
        bool rawPhysicsProp = Array.Exists(grabbables, component => component.GetType() == typeof(PhysicsProp));
        Plugin.Log.LogInfo($"{stage} | Type={expected} | Prefab={prefab.name} | Instance={prefab.GetInstanceID()} | " +
            $"BoosterBehaviour={pack != null} | PhysicsProp={rawPhysicsProp} | GrabbableComponents={grabbables.Length} | " +
            $"PackType={pack?.PackType} | ItemProperties={pack?.itemProperties?.name} | " +
            $"NetworkObject={prefab.GetComponent<NetworkObject>() != null} | Consumption={prefab.GetComponent<NetworkItemConsumption>() != null}");
        if (grabbables.Length != 1 || pack == null || grabbables[0] != pack || pack.PackType != expected ||
            pack.itemProperties == null || prefab.GetComponent<NetworkObject>() == null ||
            prefab.GetComponent<NetworkItemConsumption>() == null)
            throw new InvalidOperationException($"Invalid prepared {expected} booster layout on {prefab.name}.");
    }
}
