using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Grading;

// Present on every card prefab. Only actual returns enable the display state.
public class GradingReturnData : NetworkBehaviour
{
    private readonly NetworkVariable<Vector3> displayPosition = new();
    private readonly NetworkVariable<Quaternion> displayRotation = new();
    private readonly NetworkVariable<bool> onPedestal = new(false);
    private GradingReturnPedestalLock? pedestalLock;
    private bool pendingReturn;
    private Vector3 pendingPosition;
    private Quaternion pendingRotation = Quaternion.identity;
    private bool claimed;

    // Job IDs remain private to the server; clients only need presentation state.
    public string JobId { get; private set; } = "";

    public void Initialize(string jobId, Vector3 position, Quaternion rotation)
    {
        JobId = jobId;
        pendingPosition = position;
        pendingRotation = rotation;
        pendingReturn = true;
        claimed = false;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        onPedestal.OnValueChanged += OnDisplayChanged;
        displayPosition.OnValueChanged += OnPositionChanged;
        displayRotation.OnValueChanged += OnRotationChanged;
        if (IsServer && pendingReturn)
        {
            displayPosition.Value = pendingPosition;
            displayRotation.Value = pendingRotation;
            onPedestal.Value = true;
            pendingReturn = false;
        }
        RefreshDisplay();
    }

    public override void OnNetworkDespawn()
    {
        onPedestal.OnValueChanged -= OnDisplayChanged;
        displayPosition.OnValueChanged -= OnPositionChanged;
        displayRotation.OnValueChanged -= OnRotationChanged;
        ReleaseDisplay();
        base.OnNetworkDespawn();
    }

    private void OnDisplayChanged(bool previous, bool current) => RefreshDisplay();
    private void OnPositionChanged(Vector3 previous, Vector3 current) => RefreshDisplay();
    private void OnRotationChanged(Quaternion previous, Quaternion current) => RefreshDisplay();

    private void RefreshDisplay()
    {
        if (!onPedestal.Value)
        {
            ReleaseDisplay();
            return;
        }
        if (pedestalLock == null)
            pedestalLock = gameObject.AddComponent<GradingReturnPedestalLock>();
        pedestalLock.Initialize(displayPosition.Value, displayRotation.Value);
    }

    private void ReleaseDisplay()
    {
        if (pedestalLock == null)
            return;
        pedestalLock.enabled = false;
        Destroy(pedestalLock);
        pedestalLock = null;
    }

    public void TryClaim()
    {
        if (!IsServer || !IsSpawned || claimed || string.IsNullOrEmpty(JobId))
            return;
        GrabbableObject item = GetComponent<GrabbableObject>();
        if (item == null || !item.heldByPlayerOnServer)
            return;
        if (!GradingManager.ClaimJob(JobId))
            return;

        claimed = true;
        onPedestal.Value = false;
        ReleaseDisplay();
        Plugin.Log.LogInfo($"GRADING RETURN CLAIMED | JobId={JobId}");
    }
}
