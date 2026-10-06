using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using LethalCards.Cards;
using UnityEngine;

namespace LethalCards.Boosters;

/// <summary>Local-only cosmetic presentation. Real networked cards are never moved or replaced.</summary>
internal sealed class BoosterRevealController : MonoBehaviour
{
    private enum PresentationMode
    {
        FirstPerson,
        ObserverWorld
    }

    private const float PullDuration = 0.6f;
    private const float DisplayMoveDuration = 0.3f;
    private const float PauseBeforeFlip = 0.2f;
    private const float FlipDuration = 0.35f;
    private const float RevealHoldDuration = 0.6f;
    private const float MoveAsideDuration = 0.35f;
    private const float BetweenCardsDelay = 0.2f;
    private const float BigHitBuildUpDuration = 1.25f;
    private const float BigHitPullDuration = 1.2f;
    private const float RevealAudioTriggerPoint = 0.60f;
    private const float RevealAudioLeadTime = 0.15f;
    private const float TearStripDuration = 0.55f;
    private const float ObserverForwardOffset = 1.15f;
    private const float ObserverVerticalOffset = 1.65f;
    private const float ObserverScale = 0.65f;
    private const float ObserverPresentationDistance = 1.15f;
    private const float ObserverAudioMinDistance = 1.5f;
    private const float ObserverAudioMaxDistance = 18f;
    private const int RememberedRevealLimit = 256;

    private static readonly Dictionary<ulong, BoosterRevealController> ActiveObserverReveals = new();
    private static readonly HashSet<ulong> RememberedRevealIds = new();
    private static readonly Queue<ulong> RememberedRevealOrder = new();

    private readonly List<GameObject> presentationObjects = new();
    private readonly List<Transform> cards = new();
    private readonly List<CardRarity> rarities = new();
    private Transform? pack;
    private Transform? tearStripPivot;
    private Transform? tearStrip;
    private GameObject? fxRoot;
    private AudioSource? revealAudio;
    private AudioSource? anticipationAudio;
    private bool godPack;
    private PresentationMode presentationMode;
    private ulong revealId;
    private ulong openerClientId;
    private Vector3 frozenOpeningPosition;
    private Vector3 frozenOpeningForward;
    private Vector3 frozenOpeningUp = Vector3.up;
    private Transform? shipAnchorTransform;
    private bool revealIsShipAnchored;
    private Vector3 shipLocalAnchor;
    private Quaternion shipLocalRotation;
    private bool observerRegistered;

    internal static void Begin(
        ulong revealId, ulong packNetworkObjectId, ulong openerClientId, bool firstPerson,
        PlayerControllerB? opener, Vector3 openerPosition, Vector3 openerForward,
        bool shipAnchored, Vector3 shipLocalOpeningPosition, Quaternion shipLocalOpeningRotation,
        BoosterType type, bool isGodPack,
        string card1, int variant1, int rarity1,
        string card2, int variant2, int rarity2,
        string card3, int variant3, int rarity3)
    {
        if (!RememberRevealId(revealId))
        {
            Plugin.Log.LogWarning($"OBSERVER REVEAL BLOCKED | RevealId={revealId} | Reason=DuplicateReveal");
            return;
        }

        PresentationMode mode = firstPerson ? PresentationMode.FirstPerson : PresentationMode.ObserverWorld;
        GameObject host = new($"LethalCards{mode}BoosterReveal_{revealId}_Pack{packNetworkObjectId}");
        BoosterRevealController controller = host.AddComponent<BoosterRevealController>();
        controller.revealId = revealId;
        controller.openerClientId = openerClientId;
        controller.presentationMode = mode;

        Transform? elevatorTransform = shipAnchored
            ? StartOfRound.Instance?.elevatorTransform
            : null;
        if (mode == PresentationMode.ObserverWorld && elevatorTransform != null)
        {
            Quaternion openingRotation = elevatorTransform.rotation * shipLocalOpeningRotation;
            controller.frozenOpeningPosition = elevatorTransform.TransformPoint(shipLocalOpeningPosition);
            controller.frozenOpeningForward = openingRotation * Vector3.forward;
            controller.frozenOpeningUp = openingRotation * Vector3.up;
        }
        else
        {
            controller.frozenOpeningPosition = mode == PresentationMode.ObserverWorld && opener != null
                ? opener.transform.position
                : openerPosition;
            controller.frozenOpeningForward = HorizontalDirection(
                mode == PresentationMode.ObserverWorld && opener != null
                    ? opener.transform.forward
                    : openerForward);
        }

        if (!controller.Initialize(type, isGodPack,
            new[] { card1, card2, card3 },
            new[] { variant1, variant2, variant3 },
            new[] { rarity1, rarity2, rarity3 },
            shipAnchored))
        {
            Destroy(host);
            return;
        }

        if (mode == PresentationMode.ObserverWorld)
        {
            ActiveObserverReveals[revealId] = controller;
            controller.observerRegistered = true;
            Plugin.Log.LogInfo(
                $"OBSERVER REVEAL START | RevealId={revealId} | OpenerClientId={openerClientId} | " +
                $"FrozenPosition={controller.transform.position} | FrozenForward={controller.frozenOpeningForward} | " +
                $"FrozenRotation={controller.transform.rotation.eulerAngles} | ObserverScale={ObserverScale:F2} | " +
                $"ActiveObserverReveals={ActiveObserverReveals.Count}");
        }
    }

    internal static float CalculateRevealDuration(CardRarity rarity1, CardRarity rarity2, CardRarity rarity3)
    {
        return TearStripDuration +
            CalculateCardDuration(rarity1, false) + MoveAsideDuration + BetweenCardsDelay +
            CalculateCardDuration(rarity2, false) + MoveAsideDuration + BetweenCardsDelay +
            CalculateCardDuration(rarity3, true);
    }

    private static float CalculateCardDuration(CardRarity rarity, bool finalCard)
    {
        bool ultra = rarity == CardRarity.UltraRare;
        bool secret = rarity == CardRarity.SecretRare;
        float pull = PullDuration;
        float pause = PauseBeforeFlip;
        float flip = FlipDuration;
        float buildUp = 0f;

        if (ultra || secret)
        {
            buildUp = BigHitBuildUpDuration * (secret ? 1.35f : 1f) * (finalCard ? 1.15f : 1f);
            pull = secret ? BigHitPullDuration * 1.15f : BigHitPullDuration;
            pause = secret ? 0.75f : 0.45f;
            flip = secret ? 0.7f : 0.5f;
        }

        return buildUp + pull + DisplayMoveDuration + pause + flip + RevealHoldDuration;
    }

    private bool Initialize(
        BoosterType type, bool isGodPack, string[] cardIds, int[] variants, int[] rarityValues,
        bool shipAnchored)
    {
        Camera? presentationCamera = Camera.main;
        if (presentationMode == PresentationMode.FirstPerson && presentationCamera == null)
        {
            Plugin.Log.LogWarning("BOOSTER REVEAL SKIPPED | No local presentation camera");
            return false;
        }

        if (presentationMode == PresentationMode.FirstPerson)
        {
            transform.position = presentationCamera!.transform.position;
            transform.rotation = presentationCamera.transform.rotation;
            transform.localScale = Vector3.one;
        }
        else
        {
            InitializeObserverTransform();
        }

        godPack = isGodPack;
        CreateAudioSources();
        CreateEffects();
        CreatePack(type);

        for (int i = 0; i < cardIds.Length; i++)
        {
            CardDefinition? definition = CardDatabase.GetById(cardIds[i]);
            if (definition?.ItemAsset?.spawnPrefab == null)
                continue;
            GameObject clone = CreateVisualOnlyClone(definition.ItemAsset.spawnPrefab,
                $"LethalCardsReveal_{definition.DisplayName}");
            clone.transform.SetParent(transform, false);
            CardVariantVisuals visuals = clone.AddComponent<CardVariantVisuals>();
            visuals.ApplyVariant((CardVariant)variants[i]);
            clone.SetActive(false);
            presentationObjects.Add(clone);
            cards.Add(clone.transform);
            rarities.Add((CardRarity)rarityValues[i]);
        }

        if (cards.Count != 3)
        {
            Plugin.Log.LogWarning($"BOOSTER REVEAL SKIPPED | ResolvedCards={cards.Count}/3");
            return false;
        }

        ConfigureShipAnchor(shipAnchored);
        StartCoroutine(RevealPack());
        return true;
    }

    private void ConfigureShipAnchor(bool shipAnchored)
    {
        Vector3 worldStart = transform.position;
        Transform? elevatorTransform = shipAnchored
            ? StartOfRound.Instance?.elevatorTransform
            : null;

        revealIsShipAnchored = elevatorTransform != null;
        if (elevatorTransform != null)
        {
            shipAnchorTransform = elevatorTransform;
            shipLocalAnchor = elevatorTransform.InverseTransformPoint(worldStart);
            shipLocalRotation = Quaternion.Inverse(elevatorTransform.rotation) * transform.rotation;
        }

        Plugin.Log.LogInfo(
            $"BOOSTER REVEAL ANCHOR | RevealId={revealId} | Mode={presentationMode} | " +
            $"ShipAnchored={revealIsShipAnchored} | WorldStart={worldStart} | " +
            $"ShipLocalAnchor={(revealIsShipAnchored ? shipLocalAnchor.ToString() : "N/A")} | " +
            $"ShipLocalRotation={(revealIsShipAnchored ? shipLocalRotation.ToString() : "N/A")}");
    }

    private void LateUpdate()
    {
        if (!revealIsShipAnchored || shipAnchorTransform == null)
            return;

        transform.position = shipAnchorTransform.TransformPoint(shipLocalAnchor);
        transform.rotation = shipAnchorTransform.rotation * shipLocalRotation;
    }

    private void CreateAudioSources()
    {
        GameObject audioHost = gameObject;
        if (presentationMode == PresentationMode.ObserverWorld)
        {
            audioHost = new GameObject("ObserverRevealAudio");
            audioHost.transform.SetParent(transform, false);
            audioHost.transform.localPosition = new Vector3(0f, 0f, ObserverPresentationDistance);
        }

        revealAudio = audioHost.AddComponent<AudioSource>();
        anticipationAudio = audioHost.AddComponent<AudioSource>();
        ConfigureAudioSource(revealAudio);
        ConfigureAudioSource(anticipationAudio);
    }

    private void ConfigureAudioSource(AudioSource source)
    {
        if (presentationMode == PresentationMode.FirstPerson)
        {
            source.spatialBlend = 0f;
            return;
        }

        source.spatialBlend = 1f;
        source.minDistance = ObserverAudioMinDistance;
        source.maxDistance = ObserverAudioMaxDistance;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.dopplerLevel = 0f;
    }

    private void CreateEffects()
    {
        if (BoosterRevealAssets.RevealFxPrefab == null) return;
        fxRoot = Instantiate(BoosterRevealAssets.RevealFxPrefab, transform, false);
        fxRoot.name = "LethalCardsLocalRevealFX";
        fxRoot.transform.localPosition = new Vector3(0f, 0f, 1.15f);
        fxRoot.transform.localRotation = Quaternion.identity;
        presentationObjects.Add(fxRoot);
        foreach (AudioSource source in fxRoot.GetComponentsInChildren<AudioSource>(true))
            ConfigureAudioSource(source);
        foreach (ParticleSystem particles in fxRoot.GetComponentsInChildren<ParticleSystem>(true))
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (string expected in new[] { "HitSparkles", "RainbowHitSparkles", "UltraRevealBurst", "SecretRevealBurst",
                     "SecretRainbowRing", "UltraRevealFlash", "SecretRevealFlash", "UltraRevealGlow", "SecretRevealGlow" })
            if (FindParticles(expected) == null)
                Plugin.Log.LogWarning($"REVEAL FX CHILD MISSING | Name={expected}");
    }

    private void CreatePack(BoosterType type)
    {
        Item? item = type == BoosterType.Heavy ? Plugin.HeavyBoosterItem : Plugin.LightBoosterItem;
        if (item?.spawnPrefab == null) return;
        GameObject clone = CreateVisualOnlyClone(item.spawnPrefab, "LethalCardsLocalRevealPack");
        presentationObjects.Add(clone);
        pack = clone.transform;
        pack.SetParent(transform, false);
        pack.localPosition = new Vector3(0f, -0.42f, 1.05f);
        pack.localRotation = Quaternion.identity;
        ResolveTearStrip();
    }

    private void ResolveTearStrip()
    {
        if (pack == null) return;
        tearStripPivot = FindChild(pack, "TearStripPivot");
        tearStrip = FindChild(pack, "TearStrip");
        if (tearStripPivot == null || tearStrip == null)
            Plugin.Log.LogWarning("BOOSTER REVEAL TEAR STRIP MISSING | Expected children named TearStripPivot and TearStrip on reveal pack visual.");
        else
            tearStrip.gameObject.SetActive(true);
    }

    private static GameObject CreateVisualOnlyClone(GameObject source, string cloneName)
    {
        GameObject clone = new(cloneName);
        clone.transform.localScale = source.transform.localScale;
        CopyVisualChildren(source.transform, clone.transform);
        return clone;
    }

    private static void CopyVisualChildren(Transform source, Transform destination)
    {
        MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
        if (sourceFilter != null && sourceRenderer != null)
        {
            destination.gameObject.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer renderer = destination.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.enabled = sourceRenderer.enabled;
        }

        foreach (Transform sourceChild in source)
        {
            GameObject child = new(sourceChild.name);
            child.transform.SetParent(destination, false);
            child.transform.localPosition = sourceChild.localPosition;
            child.transform.localRotation = sourceChild.localRotation;
            child.transform.localScale = sourceChild.localScale;
            CopyVisualChildren(sourceChild, child.transform);
            child.SetActive(sourceChild.gameObject.activeSelf);
        }
    }

    private IEnumerator RevealPack()
    {
        PlayOneShot(BoosterRevealAssets.BoosterOpen, 0.7f);
        if (godPack) PlayOneShot(BoosterRevealAssets.GodPack, 1f);
        yield return AnimateTearStrip();

        yield return RevealCard(0, false);
        yield return MoveLocal(cards[0], cards[0].localPosition, new Vector3(-0.34f, 0f, 1.15f), MoveAsideDuration);
        yield return new WaitForSeconds(BetweenCardsDelay);
        yield return RevealCard(1, false);
        yield return MoveLocal(cards[1], cards[1].localPosition, new Vector3(0.34f, 0f, 1.15f), MoveAsideDuration);
        yield return new WaitForSeconds(BetweenCardsDelay);
        yield return RevealCard(2, true);
        Destroy(gameObject);
    }

    private IEnumerator RevealCard(int index, bool finalCard)
    {
        Transform card = cards[index];
        CardRarity rarity = rarities[index];
        bool ultra = rarity == CardRarity.UltraRare;
        bool secret = rarity == CardRarity.SecretRare;
        float pull = PullDuration;
        float pause = PauseBeforeFlip;
        float flip = FlipDuration;

        if (ultra || secret)
        {
            yield return BigHitBuildUp(rarity, finalCard);
            pull = secret ? BigHitPullDuration * 1.15f : BigHitPullDuration;
            pause = secret ? 0.75f : 0.45f;
            flip = secret ? 0.7f : 0.5f;
        }

        Vector3 start = new(0f, -0.42f, 1.05f);
        card.gameObject.SetActive(true);
        card.localPosition = start;
        card.localRotation = Quaternion.Euler(0f, 180f, 0f);
        yield return MoveLocal(card, start, new Vector3(0f, 0.12f, 1.15f), pull);
        yield return MoveLocal(card, card.localPosition, new Vector3(0f, 0f, 1.15f), DisplayMoveDuration);
        yield return new WaitForSeconds(pause);
        StartCoroutine(PlayRevealAudioDuringFlip(rarity, flip));
        yield return FlipLocal(card, flip);
        PlayImpact(rarity);
        if (ultra || secret) StartCoroutine(ScalePunch(card, secret ? 1.12f : 1.08f));
        yield return new WaitForSeconds(RevealHoldDuration);
    }

    private IEnumerator BigHitBuildUp(CardRarity rarity, bool finalCard)
    {
        bool secret = rarity == CardRarity.SecretRare;
        float duration = BigHitBuildUpDuration * (secret ? 1.35f : 1f) * (finalCard ? 1.15f : 1f);
        float strength = (secret ? 0.005f : 0.0025f) * (finalCard ? 1.15f : 1f);
        ParticleSystem? sparkles = FindParticles(
            secret ? "RainbowHitSparkles" : "HitSparkles");
        if (sparkles != null)
        {
            sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var emission = sparkles.emission;
            emission.rateOverTime = (secret ? 200f : 150f) * (finalCard ? 1.15f : 1f);
            sparkles.Play();
        }
        StartAnticipation(secret ? BoosterRevealAssets.SecretAnticipation : BoosterRevealAssets.UltraAnticipation, duration);
        Vector3 original = pack != null ? pack.localPosition : Vector3.zero;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (pack != null)
            {
                Vector2 offset = Random.insideUnitCircle * strength;
                pack.localPosition = original + new Vector3(offset.x, offset.y, 0f);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (pack != null) pack.localPosition = original;
        if (sparkles != null) sparkles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (anticipationAudio != null)
        {
            anticipationAudio.Stop();
            anticipationAudio.pitch = 1f;
            anticipationAudio.clip = null;
        }
    }

    private void StartAnticipation(AudioClip? clip, float duration)
    {
        if (anticipationAudio == null || clip == null) return;
        anticipationAudio.Stop();
        anticipationAudio.clip = clip;
        anticipationAudio.volume = 0.75f;
        anticipationAudio.pitch = duration > 0.01f ? Mathf.Clamp(clip.length / duration, 0.85f, 1.2f) : 1f;
        anticipationAudio.Play();
    }

    private IEnumerator PlayRevealAudioDuringFlip(CardRarity rarity, float duration)
    {
        float delay = Mathf.Max(0f, duration * RevealAudioTriggerPoint - RevealAudioLeadTime);
        if (delay > 0f) yield return new WaitForSeconds(delay);
        AudioClip? clip = rarity switch
        {
            CardRarity.Rare => BoosterRevealAssets.Rare,
            CardRarity.UltraRare => BoosterRevealAssets.Ultra,
            CardRarity.SecretRare => BoosterRevealAssets.Secret,
            _ => BoosterRevealAssets.CommonUncommon
        };
        PlayOneShot(clip, rarity >= CardRarity.UltraRare ? 1f : 0.75f);
    }

    private void PlayImpact(CardRarity rarity)
    {
        string[] names = rarity == CardRarity.UltraRare
            ? new[] { "UltraRevealBurst", "UltraRevealFlash", "UltraRevealGlow" }
            : rarity == CardRarity.SecretRare
                ? new[] { "SecretRevealBurst", "SecretRainbowRing", "SecretRevealFlash", "SecretRevealGlow" }
                : System.Array.Empty<string>();
        foreach (string particleName in names)
        {
            ParticleSystem? particles = FindParticles(particleName);
            if (particles == null) continue;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Play();
        }
    }

    private ParticleSystem? FindParticles(string particleName)
    {
        if (fxRoot == null) return null;
        foreach (ParticleSystem particles in fxRoot.GetComponentsInChildren<ParticleSystem>(true))
            if (particles.name.Equals(particleName, System.StringComparison.OrdinalIgnoreCase)) return particles;
        return null;
    }

    private void PlayOneShot(AudioClip? clip, float volume)
    {
        if (revealAudio != null && clip != null) revealAudio.PlayOneShot(clip, volume);
    }

    private static Transform? FindChild(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
                return child;
        return null;
    }

    private IEnumerator AnimateTearStrip()
    {
        if (tearStrip == null || tearStripPivot == null)
            yield break;

        tearStrip.gameObject.SetActive(true);
        tearStripPivot.SetParent(transform, true);

        Quaternion pivotStartRotation = tearStripPivot.localRotation;
        Quaternion pivotPeeledRotation = pivotStartRotation * Quaternion.Euler(-65f, 0f, 0f);
        Vector3 pivotStartPosition = tearStripPivot.localPosition;
        float peelDuration = TearStripDuration * 0.38f;
        float flingDuration = TearStripDuration - peelDuration;

        float elapsed = 0f;
        while (elapsed < peelDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / peelDuration);
            tearStripPivot.localRotation = Quaternion.Slerp(pivotStartRotation, pivotPeeledRotation, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        tearStripPivot.localRotation = pivotPeeledRotation;
        Vector3 flingStartPosition = tearStripPivot.localPosition;
        Quaternion flingStartRotation = tearStripPivot.localRotation;
        Vector3 flingEndPosition = pivotStartPosition + new Vector3(-0.85f, 0.55f, 0.12f);
        Quaternion flingEndRotation = pivotPeeledRotation * Quaternion.Euler(-25f, 0f, 120f);
        elapsed = 0f;
        while (elapsed < flingDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / flingDuration);
            tearStripPivot.localPosition = Vector3.Lerp(flingStartPosition, flingEndPosition, t);
            tearStripPivot.localRotation = Quaternion.Slerp(flingStartRotation, flingEndRotation, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        tearStripPivot.localPosition = flingEndPosition;
        tearStripPivot.localRotation = flingEndRotation;
        tearStripPivot.gameObject.SetActive(false);
    }

    private static IEnumerator MoveLocal(Transform target, Vector3 start, Vector3 end, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            target.localPosition = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            elapsed += Time.deltaTime;
            yield return null;
        }
        target.localPosition = end;
    }

    private static IEnumerator FlipLocal(Transform target, float duration)
    {
        Quaternion start = target.localRotation;
        Quaternion end = start * Quaternion.Euler(0f, 180f, 0f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            target.localRotation = Quaternion.Slerp(start, end, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            elapsed += Time.deltaTime;
            yield return null;
        }
        target.localRotation = end;
    }

    private static IEnumerator ScalePunch(Transform target, float multiplier)
    {
        yield return new WaitForSeconds(0.04f);
        Vector3 original = target.localScale;
        Vector3 punched = original * multiplier;
        yield return Scale(target, original, punched, 0.08f);
        yield return Scale(target, punched, original, 0.14f);
        target.localScale = original;
    }

    private static IEnumerator Scale(Transform target, Vector3 start, Vector3 end, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            target.localScale = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            elapsed += Time.deltaTime;
            yield return null;
        }
        target.localScale = end;
    }

    private void InitializeObserverTransform()
    {
        Vector3 revealCenter = frozenOpeningPosition +
            frozenOpeningForward * ObserverForwardOffset +
            frozenOpeningUp * ObserverVerticalOffset;

        // Match the first-person hierarchy: the root points along the opener's view
        // direction, so a revealed card at local identity faces back toward the opener.
        transform.position = revealCenter -
            frozenOpeningForward * (ObserverPresentationDistance * ObserverScale);
        transform.rotation = Quaternion.LookRotation(frozenOpeningForward, frozenOpeningUp);
        transform.localScale = Vector3.one * ObserverScale;
    }

    private static Vector3 HorizontalDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    private static bool RememberRevealId(ulong id)
    {
        if (!RememberedRevealIds.Add(id))
            return false;

        RememberedRevealOrder.Enqueue(id);
        while (RememberedRevealOrder.Count > RememberedRevealLimit)
            RememberedRevealIds.Remove(RememberedRevealOrder.Dequeue());
        return true;
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        if (anticipationAudio != null)
        {
            anticipationAudio.Stop();
            anticipationAudio.pitch = 1f;
        }
        foreach (GameObject obj in presentationObjects)
            if (obj != null) Destroy(obj);
        presentationObjects.Clear();

        if (observerRegistered)
        {
            if (ActiveObserverReveals.TryGetValue(revealId, out BoosterRevealController? active) && active == this)
                ActiveObserverReveals.Remove(revealId);
            observerRegistered = false;
            Plugin.Log.LogInfo(
                $"OBSERVER REVEAL COMPLETE | RevealId={revealId} | OpenerClientId={openerClientId} | " +
                $"RemainingObserverReveals={ActiveObserverReveals.Count}");
        }
    }
}
