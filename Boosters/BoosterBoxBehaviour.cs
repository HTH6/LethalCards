using System;
using GameNetcodeStuff;
using LethalCards.Networking;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Boosters;

public class BoosterBoxBehaviour : PhysicsProp
{
    public BoosterBoxType BoxType;
    private bool openingStarted;
    private float nextOpenRequestTime;
    internal int DiagnosticActivationCount { get; private set; }

    // Preserve a failed/partially consumed box's lock across ship saves as well.
    // Successful boxes are despawned; sealed boxes save zero. Existing pack/card saves are untouched.
    public override int GetItemDataToSave() => openingStarted ? 1 : 0;

    public override void LoadItemSaveData(int saveData)
    {
        openingStarted = saveData != 0;
    }

    public override void ItemActivate(bool used, bool buttonDown = true)
    {
        Plugin.Log.LogInfo($"BOOSTER BOX ITEM ACTIVATE | Type={BoxType} | Used={used} | ButtonDown={buttonDown} | HeldBy={playerHeldBy?.actualClientId} | Spawned={IsSpawned} | Server={IsServer} | Owner={OwnerClientId}");
        DiagnosticActivationCount++;
        base.ItemActivate(used, buttonDown);
        if (!buttonDown || !IsSpawned)
        {
            Plugin.Log.LogInfo($"BOOSTER BOX ACTIVATE SKIP | ButtonDown={buttonDown} | Spawned={IsSpawned}");
            return;
        }
        if (IsServer)
        {
            TryOpenServer(NetworkManager.Singleton.LocalClientId);
            return;
        }
        if (Time.realtimeSinceStartup < nextOpenRequestTime)
        {
            Plugin.Log.LogInfo("BOOSTER BOX ACTIVATE SKIP | Reason=Request throttle");
            return;
        }
        nextOpenRequestTime = Time.realtimeSinceStartup + 0.5f;
        RequestOpenBoxServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOpenBoxServerRpc(ServerRpcParams rpcParams = default)
    {
        Plugin.Log.LogInfo($"BOOSTER BOX SERVER RPC | Type={BoxType} | Sender={rpcParams.Receive.SenderClientId}");
        TryOpenServer(rpcParams.Receive.SenderClientId);
    }

    private void TryOpenServer(ulong senderClientId)
    {
        if (!IsServer || openingStarted || StartOfRound.Instance == null)
        {
            Plugin.Log.LogInfo($"BOOSTER BOX OPEN REJECT | Server={IsServer} | OpeningStarted={openingStarted} | RoundPresent={StartOfRound.Instance != null}");
            return;
        }
        // Unused player slots can share client ID zero. Never select the last ID match:
        // use this object's server-side holder, then authenticate it against the RPC sender.
        PlayerControllerB? holder = playerHeldBy;
        NetworkItemConsumption consumption = GetComponent<NetworkItemConsumption>();
        if (holder == null || holder.actualClientId != senderClientId ||
            !NetworkItemConsumption.IsValidHolder(holder, this) || consumption == null)
        {
            LogValidationDetail(senderClientId);
            Plugin.Log.LogInfo($"BOOSTER BOX OPEN REJECT | Sender={senderClientId} | Holder={holder?.actualClientId} | " +
                $"Controlled={holder?.isPlayerControlled} | Dead={holder?.isPlayerDead} | Spawned={IsSpawned} | " +
                $"HeldOnServer={heldByPlayerOnServer} | Owner={OwnerClientId} | " +
                $"HeldObjectMatches={(holder != null && holder.currentlyHeldObjectServer == this)} | ConsumptionPresent={consumption != null}");
            return;
        }

        // Lock before any RNG/instantiation. Failed openings cannot reroll or duplicate contents.
        openingStarted = true;
        GameObject[] packs = new GameObject[4];
        BoosterType[] types = new BoosterType[4];
        try
        {
            Plugin.Log.LogInfo($"BOOSTER BOX OPEN | Type={BoxType}");
            float halfSpacingX = 0.35f;
            float halfSpacingZ = 0.25f;
            for (int i = 0; i < packs.Length; i++)
            {
                // Four independent server rolls for Standard; Golden performs no type roll.
                types[i] = BoxType == BoosterBoxType.Golden
                    ? BoosterType.Heavy
                    : BalanceConfig.StandardBoxPackMix.Roll(UnityEngine.Random.value);
                Item? item = types[i] == BoosterType.Heavy ? Plugin.HeavyBoosterItem : Plugin.LightBoosterItem;
                if (item == null || item.spawnPrefab == null ||
                    item.spawnPrefab.GetComponent<NetworkObject>() == null ||
                    item.spawnPrefab.GetComponent<BoosterPackBehaviour>() == null)
                    throw new InvalidOperationException($"Registered {types[i]} booster prefab is unavailable.");

                BoosterPrefabDiagnostics.ValidateAndLog(item.spawnPrefab, types[i], "BOX PACK PREFAB");
                packs[i] = Instantiate(item.spawnPrefab, transform.position, Quaternion.identity,
                    StartOfRound.Instance.propsContainer);
                BoosterPrefabDiagnostics.ValidateAndLog(packs[i], types[i], "BOX PACK INSTANCE PRE-SPAWN");
                BoosterPackBehaviour pack = packs[i].GetComponent<BoosterPackBehaviour>();
                if (pack.PackType != types[i])
                    throw new InvalidOperationException("Registered booster PackType does not match box contents.");
                pack.isInFactory = isInFactory;
                pack.isInShipRoom = isInShipRoom;
                pack.isInElevator = isInElevator;
                pack.SetScrapValue(UnityEngine.Random.Range(item.minValue, item.maxValue));

                // Inspect the actual instantiated collider dimensions; no asset/collider edits.
                foreach (Collider collider in packs[i].GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled || collider.isTrigger)
                        continue;
                    halfSpacingX = Mathf.Max(halfSpacingX, collider.bounds.extents.x + 0.15f);
                    halfSpacingZ = Mathf.Max(halfSpacingZ, collider.bounds.extents.z + 0.15f);
                }
            }

            // Stage all four before spawning; no physics frame elapses at the staging position.
            for (int i = 0; i < packs.Length; i++)
            {
                packs[i].transform.position = transform.position + new Vector3(
                    i % 2 == 0 ? -halfSpacingX : halfSpacingX, 0.15f,
                    i < 2 ? -halfSpacingZ : halfSpacingZ);
                NetworkObject networkObject = packs[i].GetComponent<NetworkObject>();
                networkObject.Spawn();
                if (!networkObject.IsSpawned)
                    throw new InvalidOperationException($"Pack {i + 1} did not network-spawn.");
                BoosterPackBehaviour spawnedPack = packs[i].GetComponent<BoosterPackBehaviour>();
                Plugin.Log.LogInfo($"BOX PACK INSTANCE SPAWNED | Type={types[i]} | NetworkObjectId={networkObject.NetworkObjectId} | " +
                    $"Spawned={networkObject.IsSpawned} | BoosterBehaviour={spawnedPack != null} | PackType={spawnedPack?.PackType}");
                Plugin.Log.LogInfo($"BOX PACK {i + 1} | Type={types[i]}");
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError($"BOOSTER BOX FAILED | Type={BoxType} | Opening locked; rolling back contents. | {exception}");
            foreach (GameObject pack in packs)
            {
                if (pack == null)
                    continue;
                try
                {
                    NetworkObject networkObject = pack.GetComponent<NetworkObject>();
                    if (networkObject != null && networkObject.IsSpawned)
                        networkObject.Despawn(true);
                    else
                        Destroy(pack);
                }
                catch (Exception cleanupException)
                {
                    Plugin.Log.LogError($"BOOSTER BOX ROLLBACK FAILED | {cleanupException}");
                }
            }
            return;
        }

        // All four packs are live. Use the existing inventory cleanup + server despawn path.
        try { consumption.ConsumeServer(); }
        catch (Exception exception)
        {
            Plugin.Log.LogError($"BOOSTER BOX CONSUMPTION FAILED | Opening remains locked. | {exception}");
        }
    }

    private void LogValidationDetail(ulong senderClientId)
    {
        PlayerControllerB? holder = playerHeldBy;
        Plugin.Log.LogInfo($"BOOSTER BOX VALIDATION DETAIL | Sender={senderClientId} | Holder={holder?.actualClientId} | " +
            $"HolderInstance={holder?.GetInstanceID()} | Owner={OwnerClientId} | Controlled={holder?.isPlayerControlled} | Dead={holder?.isPlayerDead} | " +
            $"This={DescribeHeldObject(this)} | CurrentHeldServer={DescribeHeldObject(holder?.currentlyHeldObjectServer)} | " +
            $"CurrentHeldLocal={DescribeHeldObject(holder?.currentlyHeldObject)} | " +
            $"ServerMatch={ReferenceEquals(holder?.currentlyHeldObjectServer, this)} | LocalMatch={ReferenceEquals(holder?.currentlyHeldObject, this)}");
        if (StartOfRound.Instance == null)
            return;
        foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            if (player != null && player.actualClientId == senderClientId)
                Plugin.Log.LogInfo($"BOOSTER BOX PLAYER CANDIDATE | Sender={senderClientId} | Instance={player.GetInstanceID()} | " +
                    $"ActualHolder={ReferenceEquals(player, holder)} | Controlled={player.isPlayerControlled} | " +
                    $"CurrentHeldServer={DescribeHeldObject(player.currentlyHeldObjectServer)}");
    }

    private static string DescribeHeldObject(GrabbableObject? item) => item == null ? "null" :
        $"{item.GetType().Name}(Instance={item.GetInstanceID()},NetworkId={(item.IsSpawned ? item.NetworkObjectId.ToString() : "unspawned")})";
}
