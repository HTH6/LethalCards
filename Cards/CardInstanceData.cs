using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Cards;

public class CardInstanceData : NetworkBehaviour
{
    private TextMeshPro? gradeLabel;
    private const int DefaultScanMinRange = 1;
    private const int DefaultScanMaxRange = 13;

    private readonly NetworkVariable<int> networkVariant =
        new NetworkVariable<int>(
            (int)CardVariant.Standard,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<int> networkGrade =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<int> networkUngradedValue =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private string cardId = "";
    private readonly NetworkVariable<int> networkFinalValue = new(-1);
    private int pendingFinalValue = -1;

    // ============================================================
    // PENDING INITIALIZATION
    //
    // NetworkVariables should not be written before the
    // NetworkBehaviour has actually spawned.
    //
    // Initialize() / InitializeLoaded() store their values here,
    // then OnNetworkSpawn() applies them on the server.
    // ============================================================

    private bool hasPendingInitialization;

    private CardVariant pendingVariant =
        CardVariant.Standard;

    private int pendingGrade;

    private int pendingUngradedValue;

    // ============================================================

    public string CardId =>
        cardId;

    public CardVariant Variant =>
        (CardVariant)networkVariant.Value;

    public int Grade =>
        networkGrade.Value;

    public int UngradedValue =>
        networkUngradedValue.Value;

    public int FinalValue =>
        networkFinalValue.Value >= 0 ? networkFinalValue.Value :
        CardGrading.CalculateGradedValue(
            UngradedValue,
            Grade
        );

    // ============================================================
    // NETWORK LIFECYCLE
    // ============================================================

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResolveCardId();

        networkVariant.OnValueChanged += OnVariantChanged;

        networkGrade.OnValueChanged +=
            OnGradeChanged;

        networkUngradedValue.OnValueChanged += OnValueChanged;
        networkFinalValue.OnValueChanged += OnValueChanged;

        // Natural scrap spawns have no booster/return initialization.
        if (IsServer && !hasPendingInitialization)
        {
            CardDefinition? card = CardDatabase.GetById(cardId);
            if (card != null)
                InitializeLoaded(card, CardVariant.Standard, 0);
        }

        // Apply values that were prepared before
        // NetworkObject.Spawn().
        if (
            IsServer &&
            hasPendingInitialization)
        {
            ApplyPendingInitialization();
        }

        RefreshGradeVisual();
        RefreshScrapValue();
        UpdateCardDisplayName();
        RefreshVariantVisuals();
        RefreshScanNodes();

        Plugin.Log.LogInfo(
            $"CARD NETWORK SPAWN | " +
            $"CardId={CardId} | " +
            $"Variant={Variant} | " +
            $"Grade={Grade} | " +
            $"UngradedValue=${UngradedValue} | " +
            $"FinalValue=${FinalValue} | " +
            $"IsServer={IsServer}"
        );
    }

    public override void OnNetworkDespawn()
    {
        networkVariant.OnValueChanged -= OnVariantChanged;
        networkGrade.OnValueChanged -=
            OnGradeChanged;
        networkUngradedValue.OnValueChanged -= OnValueChanged;
        networkFinalValue.OnValueChanged -= OnValueChanged;

        base.OnNetworkDespawn();
    }

    private void ApplyPendingInitialization()
    {
        if (!IsServer)
            return;

        networkVariant.Value =
            (int)pendingVariant;

        networkGrade.Value =
            pendingGrade;

        networkUngradedValue.Value =
            pendingUngradedValue;
        networkFinalValue.Value = pendingFinalValue;

        hasPendingInitialization =
            false;

        Plugin.Log.LogInfo(
            $"CARD PENDING DATA APPLIED | " +
            $"CardId={CardId} | " +
            $"Variant={Variant} | " +
            $"Grade={Grade} | " +
            $"UngradedValue=${UngradedValue} | " +
            $"FinalValue=${FinalValue}"
        );
    }

    // ============================================================
    // CARD INITIALIZATION
    // ============================================================

    public void Initialize(
        CardPull pull)
    {
        if (
            pull == null ||
            pull.Card == null)
        {
            Plugin.Log.LogError(
                "CARD INSTANCE INITIALIZE FAILED | " +
                "Pull or CardDefinition was null."
            );

            return;
        }

        cardId =
            pull.Card.CardId;

        if (
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            !NetworkManager.Singleton.IsServer)
        {
            Plugin.Log.LogWarning(
                $"CARD INSTANCE INITIALIZE REJECTED | " +
                $"CardId={cardId} | " +
                $"Reason=Not server"
            );

            return;
        }

        pendingVariant =
            pull.Variant;

        pendingUngradedValue =
            pull.UngradedValue;

        pendingGrade =
            0;
        pendingFinalValue = -1;

        hasPendingInitialization =
            true;

        Plugin.Log.LogInfo(
            $"CARD INSTANCE INITIALIZED PENDING | " +
            $"CardId={CardId} | " +
            $"Variant={pendingVariant} | " +
            $"Value=${pendingUngradedValue} | " +
            $"Grade={pendingGrade}"
        );

        // This normally won't happen because cards are
        // initialized before NetworkObject.Spawn(), but it
        // makes the method safe if we ever call it afterward.
        if (
            IsSpawned &&
            IsServer)
        {
            ApplyPendingInitialization();

            RefreshGradeVisual();
            RefreshVariantVisuals();
        }
    }

    public void InitializeLoaded(
        CardDefinition card,
        CardVariant variant,
        int grade,
        int? finalValue = null)
    {
        if (card == null)
        {
            Plugin.Log.LogError(
                "CARD INSTANCE RESTORE FAILED | " +
                "CardDefinition was null."
            );

            return;
        }

        cardId =
            card.CardId;

        if (
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            !NetworkManager.Singleton.IsServer)
        {
            Plugin.Log.LogWarning(
                $"CARD INSTANCE RESTORE REJECTED | " +
                $"CardId={cardId} | " +
                $"Reason=Not server"
            );

            return;
        }

        pendingVariant =
            variant;

        pendingGrade =
            grade;
        pendingFinalValue = finalValue ?? -1;

        pendingUngradedValue =
            (int)System.Math.Round(
                card.BaseScrapValue *
                BalanceConfig.GetVariantValueMultiplier(variant)
            );

        hasPendingInitialization =
            true;

        Plugin.Log.LogInfo(
            $"CARD INSTANCE RESTORED PENDING | " +
            $"CardId={CardId} | " +
            $"Variant={pendingVariant} | " +
            $"Value=${pendingUngradedValue} | " +
            $"Grade={pendingGrade}"
        );

        // Handles any future case where restoration happens
        // after the object has already spawned.
        if (
            IsSpawned &&
            IsServer)
        {
            ApplyPendingInitialization();

            RefreshGradeVisual();
            RefreshVariantVisuals();
        }
    }

    // ============================================================
    // CARD ID
    // ============================================================

    private void ResolveCardId()
    {
        if (!string.IsNullOrEmpty(cardId))
            return;

        GrabbableObject grabbable =
            GetComponent<GrabbableObject>();

        if (
            grabbable == null ||
            grabbable.itemProperties == null)
        {
            Plugin.Log.LogWarning(
                "CARD ID RESOLVE FAILED | " +
                "Missing GrabbableObject or itemProperties."
            );

            return;
        }

        CardDefinition? card =
            CardDatabase.GetByItem(
                grabbable.itemProperties
            );

        if (card == null)
        {
            Plugin.Log.LogWarning(
                $"CARD ID RESOLVE FAILED | " +
                $"Item={grabbable.itemProperties.itemName}"
            );

            return;
        }

        cardId =
            card.CardId;
    }

    // ============================================================
    // GRADE VISUAL
    // ============================================================

    private void OnGradeChanged(
        int oldGrade,
        int newGrade)
    {
        RefreshGradeVisual();
        RefreshScrapValue();
        RefreshScanNodes();
    }

    private void OnValueChanged(int previous, int current)
    {
        RefreshScrapValue();
        RefreshScanNodes();
    }

    private void OnVariantChanged(int previous, int current)
    {
        UpdateCardDisplayName();
        RefreshVariantVisuals();
        RefreshScanNodes();
    }

    private void RefreshVariantVisuals()
    {
        CardVariantVisuals visuals = GetComponent<CardVariantVisuals>();
        if (visuals != null)
            visuals.ApplyVariant(Variant);
    }

    private void UpdateCardDisplayName()
    {
        ResolveCardId();
        CardDefinition? card = CardDatabase.GetById(cardId);
        if (card == null)
            return;

        string displayName = CardNameFormatter.GetDisplayName(card.DisplayName, Variant);
        // Scan nodes belong to this spawned instance. Never rename the shared Item:
        // it is also used by registry lookup and vanilla save/load identity.
        foreach (ScanNodeProperties scanNode in GetComponentsInChildren<ScanNodeProperties>(true))
            scanNode.headerText = displayName;
    }

    private void LateUpdate()
    {
        // Vanilla spawn/load scrap synchronization can run after OnNetworkSpawn.
        // Keep the physical value consistent with the server-owned metadata.
        if (IsSpawned)
            RefreshScrapValue();
    }

    private void RefreshScrapValue()
    {
        GrabbableObject item = GetComponent<GrabbableObject>();
        if (item != null && item.scrapValue != FinalValue)
            item.SetScrapValue(FinalValue);
    }

    private void RefreshScanNodes()
    {
        ResolveCardId();
        CardDefinition? card = CardDatabase.GetById(cardId);
        string displayName = card != null
            ? CardNameFormatter.GetDisplayName(card.DisplayName, Variant)
            : "Lethal Card";
        int scanNodeLayer = LayerMask.NameToLayer("ScanNode");

        foreach (ScanNodeProperties scanNode in GetComponentsInChildren<ScanNodeProperties>(true))
        {
            scanNode.gameObject.SetActive(true);
            scanNode.enabled = true;
            if (scanNodeLayer >= 0)
                scanNode.gameObject.layer = scanNodeLayer;
            if (scanNode.minRange <= 0)
                scanNode.minRange = DefaultScanMinRange;
            if (scanNode.maxRange <= 0)
                scanNode.maxRange = DefaultScanMaxRange;
            scanNode.headerText = displayName;
            scanNode.subText = $"Value: ${FinalValue}";
            scanNode.scrapValue = FinalValue;
        }
    }

    private void RefreshGradeVisual()
    {
        if (Grade <= 0)
        {
            if (gradeLabel != null)
            {
                Destroy(
                    gradeLabel.gameObject
                );

                gradeLabel = null;
            }

            return;
        }

        if (gradeLabel == null)
        {
            CreateGradeLabel();
        }

        if (gradeLabel == null)
            return;

        gradeLabel.text =
            $"GRADE {Grade}";

        gradeLabel.gameObject.SetActive(
            true
        );
    }

    private void CreateGradeLabel()
    {
        GameObject labelObject =
            new GameObject(
                "LethalCardsGradeLabel"
            );

        labelObject.transform.SetParent(
            transform,
            false
        );

        labelObject.transform.localPosition =
            new Vector3(
                0f,
                0.30f,
                -0.60f
            );

        labelObject.transform.localRotation =
            Quaternion.identity;

        labelObject.transform.localScale =
            new Vector3(
                0.12f,
                0.12f,
                0.12f
            );

        gradeLabel =
            labelObject.AddComponent<TextMeshPro>();

        gradeLabel.alignment =
            TextAlignmentOptions.Center;

        gradeLabel.fontSize =
            4f;

        gradeLabel.enableAutoSizing =
            false;

        gradeLabel.text =
            $"GRADE {Grade}";

        gradeLabel.color =
            Color.white;

        gradeLabel.rectTransform.sizeDelta =
            new Vector2(
                5f,
                1f
            );

        gradeLabel.enableWordWrapping =
            false;

        Plugin.Log.LogInfo(
            $"CARD GRADE LABEL CREATED | " +
            $"CardId={CardId} | " +
            $"Grade={Grade}"
        );
    }

    // ============================================================
    // DIRECT GRADING
    //
    // Currently retained because other code may reference it.
    // The grading pedestal itself uses GradingManager jobs.
    // ============================================================

    public int GradeCard()
    {
        if (!IsServer)
        {
            Plugin.Log.LogWarning(
                $"CARD GRADE REJECTED | " +
                $"CardId={CardId} | " +
                $"Reason=Not server"
            );

            return FinalValue;
        }

        networkGrade.Value =
            CardGrading.RollGrade();
        networkFinalValue.Value = -1;

        int finalValue =
            FinalValue;

        RefreshGradeVisual();

        Plugin.Log.LogInfo(
            $"CARD GRADED | " +
            $"CardId={CardId} | " +
            $"Variant={Variant} | " +
            $"Grade={Grade} | " +
            $"Ungraded=${UngradedValue} | " +
            $"Multiplier=" +
            $"{CardGrading.GetMultiplier(Grade)}x | " +
            $"Final=${finalValue}"
        );

        return finalValue;
    }
}
