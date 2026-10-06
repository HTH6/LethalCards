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
        if (buttonDown && BoosterInspectionGuard.IsInspectingThis(playerHeldBy, this))
            return;

        // Plugin.Log.LogInfo($"BOOSTER BOX ITEM ACTIVATE | Type={BoxType} | Used={used} | ButtonDown={buttonDown} | HeldBy={playerHeldBy?.actualClientId} | Spawned={IsSpawned} | Server={IsServer} | Owner={OwnerClientId}");
        DiagnosticActivationCount++;
        base.ItemActivate(used, buttonDown);
        if (!buttonDown || !IsSpawned)
        {
            // Plugin.Log.LogInfo($"BOOSTER BOX ACTIVATE SKIP | ButtonDown={buttonDown} | Spawned={IsSpawned}");
            return;
        }
        if (IsServer)
        {
            TryOpenServer(NetworkManager.Singleton.LocalClientId);
            return;
        }
        if (Time.realtimeSinceStartup < nextOpenRequestTime)
        {
            // Plugin.Log.LogInfo("BOOSTER BOX ACTIVATE SKIP | Reason=Request throttle");
            return;
        }
        nextOpenRequestTime = Time.realtimeSinceStartup + 0.5f;
        RequestOpenBoxServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOpenBoxServerRpc(ServerRpcParams rpcParams = default)
    {
        // Plugin.Log.LogInfo($"BOOSTER BOX SERVER RPC | Type={BoxType} | Sender={rpcParams.Receive.SenderClientId}");
        TryOpenServer(rpcParams.Receive.SenderClientId);
    }

    private void TryOpenServer(ulong senderClientId)
    {
        if (!IsServer || openingStarted || StartOfRound.Instance == null)
        {
            // Plugin.Log.LogInfo($"BOOSTER BOX OPEN REJECT | Server={IsServer} | OpeningStarted={openingStarted} | RoundPresent={StartOfRound.Instance != null}");
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
            /* Plugin.Log.LogInfo($"BOOSTER BOX OPEN REJECT | Sender={senderClientId} | Holder={holder?.actualClientId} | " +
                $"Controlled={holder?.isPlayerControlled} | Dead={holder?.isPlayerDead} | Spawned={IsSpawned} | " +
                $"HeldOnServer={heldByPlayerOnServer} | Owner={OwnerClientId} | " +
                $"HeldObjectMatches={(holder != null && holder.currentlyHeldObjectServer == this)} | ConsumptionPresent={consumption != null}"); */
            return;
        }

        if (BoosterInspectionGuard.IsInspectingThis(holder, this))
            return;

        // Lock before any RNG/instantiation. Failed openings cannot reroll or duplicate contents.
        openingStarted = true;
        GameObject[] packs = new GameObject[4];
        BoosterType[] types = new BoosterType[4];
        int[] scrapValues = new int[4];
        try
        {
            Plugin.Log.LogInfo($"BOOSTER BOX OPEN | Type={BoxType}");
            Transform? shipParent = StartOfRound.Instance.elevatorTransform;
            bool boxShipState = isInElevator && isInShipRoom;
            bool holderShipState = holder.isInElevator && holder.isInHangarShipRoom;
            bool shipDetected = shipParent != null && (boxShipState || holderShipState);
            Plugin.Log.LogInfo(
                $"BOOSTER BOX SHIP CONTEXT | Type={BoxType} | BoxInElevator={isInElevator} | " +
                $"BoxInShipRoom={isInShipRoom} | HolderInElevator={holder.isInElevator} | " +
                $"HolderInHangarShipRoom={holder.isInHangarShipRoom} | ShipDetected={shipDetected}");

            Vector3 shipLocalSpawnOrigin = shipDetected
                ? shipParent!.InverseTransformPoint(transform.position)
                : Vector3.zero;
            Vector3 shipLocalLayoutRight = shipDetected
                ? shipParent!.InverseTransformDirection(Vector3.right).normalized
                : Vector3.right;
            Vector3 shipLocalLayoutUp = shipDetected
                ? shipParent!.InverseTransformDirection(Vector3.up).normalized
                : Vector3.up;
            Vector3 shipLocalLayoutForward = shipDetected
                ? shipParent!.InverseTransformDirection(Vector3.forward).normalized
                : Vector3.forward;

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

                // BoosterPrefabDiagnostics.ValidateAndLog(item.spawnPrefab, types[i], "BOX PACK PREFAB");
                Vector3 initialPosition = shipDetected
                    ? shipParent!.TransformPoint(shipLocalSpawnOrigin)
                    : transform.position;
                Transform initialParent = shipDetected
                    ? shipParent!
                    : StartOfRound.Instance.propsContainer;
                packs[i] = Instantiate(
                    item.spawnPrefab,
                    initialPosition,
                    Quaternion.identity,
                    initialParent);
                // BoosterPrefabDiagnostics.ValidateAndLog(packs[i], types[i], "BOX PACK INSTANCE PRE-SPAWN");
                BoosterPackBehaviour pack = packs[i].GetComponent<BoosterPackBehaviour>();
                if (pack.PackType != types[i])
                    throw new InvalidOperationException("Registered booster PackType does not match box contents.");
                pack.SpawnedFromBox = true;
                pack.itemProperties = item;
                pack.isInFactory = isInFactory;
                pack.isInShipRoom = shipDetected || isInShipRoom;
                pack.isInElevator = shipDetected || isInElevator;
                scrapValues[i] = UnityEngine.Random.Range(item.minValue, item.maxValue);
                pack.SetScrapValue(scrapValues[i]);

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
                float layoutX = i % 2 == 0 ? -halfSpacingX : halfSpacingX;
                float layoutZ = i < 2 ? -halfSpacingZ : halfSpacingZ;
                Vector3 originalPosition = shipDetected
                    ? shipParent!.TransformPoint(shipLocalSpawnOrigin) +
                      shipParent.TransformDirection(shipLocalLayoutRight) * layoutX +
                      shipParent.TransformDirection(shipLocalLayoutUp) * 0.15f +
                      shipParent.TransformDirection(shipLocalLayoutForward) * layoutZ
                    : transform.position + new Vector3(layoutX, 0.15f, layoutZ);
                packs[i].transform.position = originalPosition;
                BoosterPackBehaviour pack = packs[i].GetComponent<BoosterPackBehaviour>();
                Vector3? shipLocalRestPosition = null;
                if (shipDetected)
                {
                    Vector3 worldFloorPosition = pack.GetItemFloorPosition(originalPosition);
                    packs[i].transform.position = worldFloorPosition;
                    shipLocalRestPosition = shipParent!.InverseTransformPoint(worldFloorPosition);
                    Quaternion shipLocalRestRotation = Quaternion.Euler(
                        pack.itemProperties.restingRotation.x,
                        pack.floorYRot + pack.itemProperties.restingRotation.y,
                        pack.itemProperties.restingRotation.z);
                    packs[i].transform.localRotation = shipLocalRestRotation;
                    pack.ApplyInitialShipRestState(shipLocalRestPosition.Value);
                    pack.InitializeBoxShipPlacement(
                        shipLocalRestPosition.Value,
                        shipLocalRestRotation);
                }
                else
                {
                    _ = CustomSpawnPlacement.PrepareSpawnHeight(
                        packs[i],
                        originalPosition,
                        CustomSpawnPlacement.BoosterPackVerticalClearance,
                        out _);
                }

                /* Plugin.Log.LogInfo(
                    $"CUSTOM PACK SPAWN PLACEMENT | Type={types[i]} | " +
                    $"OriginalPosition={originalPosition} | CorrectedPosition={correctedPosition} | " +
                    $"VerticalAdjustment={verticalAdjustment} | BoundsMinimumY={boundsMinimumY}"); */

                NetworkObject networkObject = packs[i].GetComponent<NetworkObject>();
                CustomSpawnPlacement.MarkForFloorTargetCorrection(pack);
                networkObject.Spawn();
                if (!networkObject.IsSpawned)
                    throw new InvalidOperationException($"Pack {i + 1} did not network-spawn.");
                if (shipLocalRestPosition.HasValue)
                {
                    Rigidbody? body = pack.propBody != null ? pack.propBody : pack.GetComponent<Rigidbody>();
                    Plugin.Log.LogInfo(
                        $"BOX PACK SHIP SPAWN | BoxType={BoxType} | PackType={types[i]} | " +
                        $"NetworkObjectId={networkObject.NetworkObjectId} | ShipDetected=True | " +
                        $"WorldPosition={packs[i].transform.position} | " +
                        $"ShipLocalPosition={shipLocalRestPosition.Value} | Parent={packs[i].transform.parent?.name ?? "<none>"} | " +
                        $"IsInElevator={pack.isInElevator} | IsInShipRoom={pack.isInShipRoom} | " +
                        $"TargetFloorPosition={pack.targetFloorPosition} | " +
                        $"RigidbodyVelocity={(body != null ? body.velocity.ToString() : "<none>")}");
                }
                // Plugin.Log.LogInfo($"BOX PACK INSTANCE SPAWNED | Type={types[i]} | NetworkObjectId={networkObject.NetworkObjectId} | " +
                //     $"Spawned={networkObject.IsSpawned} | BoosterBehaviour={spawnedPack != null} | PackType={spawnedPack?.PackType}");
                // Plugin.Log.LogInfo($"BOX PACK {i + 1} | Type={types[i]}");
            }

            if (RoundManager.Instance == null)
                throw new InvalidOperationException("RoundManager is unavailable for box pack scrap-value synchronization.");

            NetworkObjectReference[] packReferences = new NetworkObjectReference[packs.Length];
            for (int i = 0; i < packs.Length; i++)
                packReferences[i] = new NetworkObjectReference(packs[i].GetComponent<NetworkObject>());
            RoundManager.Instance.SyncScrapValuesClientRpc(packReferences, scrapValues);
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
        /* Plugin.Log.LogInfo($"BOOSTER BOX VALIDATION DETAIL | Sender={senderClientId} | Holder={holder?.actualClientId} | " +
            $"HolderInstance={holder?.GetInstanceID()} | Owner={OwnerClientId} | Controlled={holder?.isPlayerControlled} | Dead={holder?.isPlayerDead} | " +
            $"This={DescribeHeldObject(this)} | CurrentHeldServer={DescribeHeldObject(holder?.currentlyHeldObjectServer)} | " +
            $"CurrentHeldLocal={DescribeHeldObject(holder?.currentlyHeldObject)} | " +
            $"ServerMatch={ReferenceEquals(holder?.currentlyHeldObjectServer, this)} | LocalMatch={ReferenceEquals(holder?.currentlyHeldObject, this)}"); */
        if (StartOfRound.Instance == null)
            return;
        foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            if (player != null && player.actualClientId == senderClientId)
            {
                // Plugin.Log.LogInfo($"BOOSTER BOX PLAYER CANDIDATE | Sender={senderClientId} | Instance={player.GetInstanceID()} | " +
                //     $"ActualHolder={ReferenceEquals(player, holder)} | Controlled={player.isPlayerControlled} | " +
                //     $"CurrentHeldServer={DescribeHeldObject(player.currentlyHeldObjectServer)}");
            }
    }

    private static string DescribeHeldObject(GrabbableObject? item) => item == null ? "null" :
        $"{item.GetType().Name}(Instance={item.GetInstanceID()},NetworkId={(item.IsSpawned ? item.NetworkObjectId.ToString() : "unspawned")})";
}
