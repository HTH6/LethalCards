using System.Collections;
using System;
using LethalCards.Cards;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LethalCards.Grading;

/// <summary>Local-only presentation for the animated grading submission pedestal.</summary>
internal sealed class GradingPedestalAnimationController : MonoBehaviour
{
    private const string IdleText = "> SUBMIT CARD FOR GRADING";
    private const float ScanningDelay = 0.35f;
    private const float ScanDuration = 3f;
    private const float PostScanPause = 0.25f;
    private const float SlotOpenDuration = 0.12f;
    private const float DropDuration = 0.45f;
    private const float SuccessDuration = 0.8f;
    private const float InsufficientCreditsDuration = 1.25f;
    private const float CursorBlinkInterval = 0.5f;
    private const float FlickerSpeed = 18f;
    private const float FlickerAmount = 0.04f;
    private const float MinGlitchInterval = 4f;
    private const float MaxGlitchInterval = 9f;
    private const float GlitchDuration = 0.06f;
    private const float GlitchBrightness = 0.35f;
    private const float TextGlitchOffset = .05f;
    private const float TextGlitchStep = 0.015f;

    private static readonly Color InsufficientCreditsEmission = new(4f, 0f, 0f, 1f);

    private Transform? cardSubmissionAnchor;
    private GameObject? cardSlotOpening;
    private TMP_Text? screenText;
    private GameObject? statusGreen;
    private GameObject? statusRed;
    private Transform? scanStartPoint;
    private Transform? scanEndPoint;
    private Transform? cardDropPoint;
    private Transform? scanFanPivot;
    private GameObject? scanBeamFan;
    private Light? scanSpotLight;
    private GameObject? scanBeam;
    private AudioSource? scanAudioSource;
    private AudioSource? cardDropAudioSource;
    private Material? screenMaterial;
    private RectTransform? screenTextRect;
    private GameObject? presentationCard;
    private Coroutine? animationRoutine;
    private Coroutine? glitchRoutine;
    private Color idleEmissionColor;
    private Color currentBaseEmissionColor;
    private Vector2 screenTextBasePosition;
    private Vector3 cardSlotFullScale;
    private Quaternion scanFanBaseRotation;
    private float nextGlitchTime;
    private float glitchMultiplier = 1f;
    private float cursorBlinkTimer;
    private bool cursorVisible = true;
    private bool initialized;

    internal bool Initialize()
    {
        if (initialized)
            return true;

        cardSubmissionAnchor = ResolveTransform("CardSubmissionAnchor");
        cardSlotOpening = ResolveObject("CardSlotOpening");
        Transform? displayScreen = ResolveTransform("DisplayScreen");
        Transform? screenTextTransform = ResolveTransform("ScreenText");
        screenText = screenTextTransform?.GetComponent<TMP_Text>() ??
                     screenTextTransform?.GetComponentInChildren<TMP_Text>(true);
        if (screenText == null)
            WarnMissing("ScreenText TMP component");

        Renderer? screenRenderer = displayScreen?.GetComponent<Renderer>() ??
                                   displayScreen?.GetComponentInChildren<Renderer>(true);
        if (screenRenderer == null)
            WarnMissing("DisplayScreen renderer");
        else
        {
            screenMaterial = screenRenderer.material;
            if (screenMaterial.HasProperty("_EmissiveColor"))
            {
                idleEmissionColor = screenMaterial.GetColor("_EmissiveColor");
                currentBaseEmissionColor = idleEmissionColor;
            }
        }

        statusGreen = ResolveObject("StatusGreen");
        statusRed = ResolveObject("StatusRed");
        scanStartPoint = ResolveTransform("ScanStartPoint");
        scanEndPoint = ResolveTransform("ScanEndPoint");
        cardDropPoint = ResolveTransform("CardDropPoint");
        scanFanPivot = ResolveTransform("ScanFanPivot");
        scanBeamFan = ResolveObject("ScanBeamFan");
        Transform? spotLightTransform = ResolveTransform("ScanSpotLight");
        scanSpotLight = spotLightTransform?.GetComponent<Light>() ??
                        spotLightTransform?.GetComponentInChildren<Light>(true);
        if (scanSpotLight == null)
            WarnMissing("ScanSpotLight Light component");
        scanBeam = ResolveObject("ScanBeam");
        scanAudioSource = ResolveAudioSource("ScanAudioSource");
        cardDropAudioSource = ResolveAudioSource("CardDropAudioSource");

        if (cardSlotOpening != null)
            cardSlotFullScale = cardSlotOpening.transform.localScale;
        if (scanFanPivot != null)
            scanFanBaseRotation = scanFanPivot.localRotation;
        if (screenText != null)
        {
            screenTextRect = screenText.rectTransform;
            screenTextBasePosition = screenTextRect.anchoredPosition;
        }

        nextGlitchTime = Time.time + Random.Range(MinGlitchInterval, MaxGlitchInterval);
        initialized = true;
        SetIdleState();
        Plugin.Log.LogInfo("GRADING PEDESTAL ANIMATION READY");
        return true;
    }

    internal void PlaySubmission(string cardId, CardVariant variant)
    {
        if (!initialized)
            Initialize();
        if (animationRoutine != null)
            return;

        CardDefinition? card = CardDatabase.GetById(cardId);
        if (card?.ItemAsset?.spawnPrefab == null || cardSubmissionAnchor == null || cardDropPoint == null)
        {
            Plugin.Log.LogWarning($"GRADING PEDESTAL PRESENTATION SKIPPED | CardId={cardId} | Missing card prefab or animation anchor");
            return;
        }

        presentationCard = CreateVisualOnlyCard(card.ItemAsset.spawnPrefab, variant);
        if (presentationCard == null)
        {
            Plugin.Log.LogWarning($"GRADING PEDESTAL PRESENTATION SKIPPED | CardId={cardId} | Visual clone failed");
            return;
        }

        presentationCard.transform.position = cardSubmissionAnchor.position;
        presentationCard.transform.rotation = cardSubmissionAnchor.rotation;
        animationRoutine = StartCoroutine(SubmissionSequence());
    }

    internal void PlayInsufficientCredits()
    {
        if (!initialized)
            Initialize();
        if (animationRoutine != null)
            return;
        animationRoutine = StartCoroutine(InsufficientCreditsSequence());
    }

    private void Update()
    {
        if (!initialized)
            return;

        if (animationRoutine == null)
        {
            cursorBlinkTimer += Time.deltaTime;
            if (cursorBlinkTimer >= CursorBlinkInterval)
            {
                cursorBlinkTimer = 0f;
                cursorVisible = !cursorVisible;
                if (screenText != null)
                    screenText.text = IdleText + (cursorVisible ? "_" : string.Empty);
            }
        }

        if (Time.time >= nextGlitchTime)
        {
            if (glitchRoutine == null)
                glitchRoutine = StartCoroutine(ScreenGlitch());
            nextGlitchTime = Time.time + Random.Range(MinGlitchInterval, MaxGlitchInterval);
        }

        if (screenMaterial != null && screenMaterial.HasProperty("_EmissiveColor"))
        {
            float noise = Mathf.PerlinNoise(Time.time * FlickerSpeed, 0f);
            float brightness = 1f + (noise - 0.5f) * 2f * FlickerAmount;
            screenMaterial.SetColor("_EmissiveColor", currentBaseEmissionColor * brightness * glitchMultiplier);
        }
    }

    private IEnumerator SubmissionSequence()
    {
        SetScannerState(false);
        SetStatus(false, true);
        SetScreenText("SCANNING");
        yield return new WaitForSeconds(ScanningDelay);

        if (scanBeam != null && scanStartPoint != null)
            scanBeam.transform.position = scanStartPoint.position;
        SetScannerState(true);
        scanAudioSource?.Play();

        float elapsed = 0f;
        while (elapsed < ScanDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / ScanDuration);
            int dots = Mathf.Clamp(Mathf.FloorToInt(t * 4f), 0, 3);
            SetScreenText("SCANNING" + new string('.', dots));

            if (scanBeam != null && scanStartPoint != null && scanEndPoint != null)
            {
                Vector3 scanPosition = Vector3.Lerp(scanStartPoint.position, scanEndPoint.position, t);
                scanBeam.transform.position = scanPosition;
                AimScanFan(scanPosition);
            }
            yield return null;
        }

        if (scanBeam != null && scanEndPoint != null)
            scanBeam.transform.position = scanEndPoint.position;
        SetScreenText("SCANNING...");
        yield return new WaitForSeconds(PostScanPause);

        SetScannerState(false);
        scanAudioSource?.Stop();
        yield return AnimateCardSlot(true);

        if (presentationCard != null && cardDropPoint != null)
        {
            Vector3 start = presentationCard.transform.position;
            Quaternion rotation = presentationCard.transform.rotation;
            cardDropAudioSource?.Play();
            elapsed = 0f;
            while (elapsed < DropDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / DropDuration);
                t *= t;
                presentationCard.transform.position = Vector3.Lerp(start, cardDropPoint.position, t);
                presentationCard.transform.rotation = rotation;
                yield return null;
            }
        }

        DestroyPresentationCard();
        yield return AnimateCardSlot(false);
        SetStatus(true, false);
        SetScreenText("SUBMISSION COMPLETE!");
        yield return new WaitForSeconds(SuccessDuration);

        animationRoutine = null;
        SetIdleState();
    }

    private IEnumerator InsufficientCreditsSequence()
    {
        SetScannerState(false);
        scanAudioSource?.Stop();
        SetStatus(false, true);
        currentBaseEmissionColor = InsufficientCreditsEmission;
        ApplyBaseEmission();
        SetScreenText("NOT ENOUGH\nCREDITS");
        yield return new WaitForSeconds(InsufficientCreditsDuration);
        currentBaseEmissionColor = idleEmissionColor;
        ApplyBaseEmission();
        animationRoutine = null;
        SetIdleState();
    }

    private IEnumerator ScreenGlitch()
    {
        glitchMultiplier = GlitchBrightness;
        if (screenTextRect != null)
        {
            float elapsed = 0f;
            while (elapsed < GlitchDuration)
            {
                screenTextRect.anchoredPosition = screenTextBasePosition + new Vector2(
                    Random.Range(-TextGlitchOffset, TextGlitchOffset),
                    Random.Range(-TextGlitchOffset * 0.25f, TextGlitchOffset * 0.25f));
                yield return new WaitForSeconds(TextGlitchStep);
                elapsed += TextGlitchStep;
            }
            screenTextRect.anchoredPosition = screenTextBasePosition;
        }
        else
        {
            yield return new WaitForSeconds(GlitchDuration);
        }

        glitchMultiplier = 1f;
        glitchRoutine = null;
    }

    private IEnumerator AnimateCardSlot(bool open)
    {
        if (cardSlotOpening == null)
            yield break;

        Vector3 closedScale = new(0.01f, cardSlotFullScale.y, 0.01f);
        Vector3 start = open ? closedScale : cardSlotFullScale;
        Vector3 end = open ? cardSlotFullScale : closedScale;
        if (open)
            cardSlotOpening.SetActive(true);
        cardSlotOpening.transform.localScale = start;

        float elapsed = 0f;
        while (elapsed < SlotOpenDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / SlotOpenDuration));
            cardSlotOpening.transform.localScale = Vector3.Lerp(start, end, t);
            yield return null;
        }

        cardSlotOpening.transform.localScale = end;
        if (!open)
        {
            cardSlotOpening.SetActive(false);
            cardSlotOpening.transform.localScale = cardSlotFullScale;
        }
    }

    private void AimScanFan(Vector3 scanPosition)
    {
        if (scanFanPivot?.parent == null)
            return;
        Vector3 worldDirection = (scanPosition - scanFanPivot.position).normalized;
        Vector3 localDirection = scanFanPivot.parent.InverseTransformDirection(worldDirection);
        scanFanPivot.localRotation = Quaternion.FromToRotation(Vector3.down, localDirection);
    }

    private void SetScannerState(bool active)
    {
        if (scanBeam != null)
            scanBeam.SetActive(active);
        if (scanBeamFan != null)
            scanBeamFan.SetActive(active);
        if (scanSpotLight != null)
            scanSpotLight.enabled = active;
    }

    private void SetStatus(bool green, bool red)
    {
        if (statusGreen != null)
            statusGreen.SetActive(green);
        if (statusRed != null)
            statusRed.SetActive(red);
    }

    private void SetScreenText(string value)
    {
        if (screenText != null)
            screenText.text = value;
    }

    private void ApplyBaseEmission()
    {
        if (screenMaterial != null && screenMaterial.HasProperty("_EmissiveColor"))
            screenMaterial.SetColor("_EmissiveColor", currentBaseEmissionColor);
    }

    private void SetIdleState()
    {
        SetScannerState(false);
        scanAudioSource?.Stop();
        DestroyPresentationCard();
        if (scanFanPivot != null)
            scanFanPivot.localRotation = scanFanBaseRotation;
        if (cardSlotOpening != null)
        {
            cardSlotOpening.SetActive(false);
            if (cardSlotFullScale != Vector3.zero)
                cardSlotOpening.transform.localScale = cardSlotFullScale;
        }
        SetStatus(true, false);
        currentBaseEmissionColor = idleEmissionColor;
        ApplyBaseEmission();
        if (screenTextRect != null)
            screenTextRect.anchoredPosition = screenTextBasePosition;
        cursorBlinkTimer = 0f;
        cursorVisible = true;
        SetScreenText(IdleText + "_");
    }

    private void CleanupPresentation()
    {
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);
        if (glitchRoutine != null)
            StopCoroutine(glitchRoutine);
        animationRoutine = null;
        glitchRoutine = null;
        glitchMultiplier = 1f;
        scanAudioSource?.Stop();
        cardDropAudioSource?.Stop();
        DestroyPresentationCard();
        SetScannerState(false);
        if (scanFanPivot != null)
            scanFanPivot.localRotation = scanFanBaseRotation;
        if (screenTextRect != null)
            screenTextRect.anchoredPosition = screenTextBasePosition;
        if (initialized)
            SetIdleState();
    }

    private GameObject? CreateVisualOnlyCard(GameObject source, CardVariant variant)
    {
        GameObject clone = new("LethalCardsGradingPresentationCard");
        try
        {
            clone.transform.localScale = source.transform.localScale;
            CopyVisualHierarchy(source.transform, clone.transform);
            CardVariantVisuals visuals = clone.AddComponent<CardVariantVisuals>();
            visuals.ApplyVariant(variant);
            return clone;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"GRADING PEDESTAL PRESENTATION CLONE FAILED | {exception}");
            Destroy(clone);
            return null;
        }
    }

    private static void CopyVisualHierarchy(Transform source, Transform destination)
    {
        MeshFilter? sourceFilter = source.GetComponent<MeshFilter>();
        MeshRenderer? sourceRenderer = source.GetComponent<MeshRenderer>();
        if (sourceFilter != null && sourceRenderer != null)
        {
            destination.gameObject.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer renderer = destination.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.enabled = sourceRenderer.enabled;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;
        }

        foreach (Transform sourceChild in source)
        {
            GameObject child = new(sourceChild.name);
            child.transform.SetParent(destination, false);
            child.transform.localPosition = sourceChild.localPosition;
            child.transform.localRotation = sourceChild.localRotation;
            child.transform.localScale = sourceChild.localScale;
            CopyVisualHierarchy(sourceChild, child.transform);
            child.SetActive(sourceChild.gameObject.activeSelf);
        }
    }

    private void DestroyPresentationCard()
    {
        if (presentationCard != null)
            Destroy(presentationCard);
        presentationCard = null;
    }

    private Transform? ResolveTransform(string childName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
                return child;
        WarnMissing(childName);
        return null;
    }

    private GameObject? ResolveObject(string childName) => ResolveTransform(childName)?.gameObject;

    private AudioSource? ResolveAudioSource(string childName)
    {
        Transform? child = ResolveTransform(childName);
        AudioSource? source = child?.GetComponent<AudioSource>();
        if (child != null && source == null)
            WarnMissing($"{childName} AudioSource");
        return source;
    }

    private static void WarnMissing(string name) =>
        Plugin.Log.LogWarning($"GRADING PEDESTAL ANIMATION CHILD MISSING | Name={name}");

    private void OnDisable() => CleanupPresentation();

    private void OnDestroy()
    {
        CleanupPresentation();
        if (screenMaterial != null)
            Destroy(screenMaterial);
    }
}
