using System.Collections;
using System.Collections.Generic;
using LethalCards.Cards;
using UnityEngine;

namespace LethalCards.Boosters;

/// <summary>Local-only cosmetic presentation. Real networked cards are never moved or replaced.</summary>
internal sealed class BoosterRevealController : MonoBehaviour
{
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

    internal static void Begin(BoosterType type, bool isGodPack,
        string card1, int variant1, int rarity1,
        string card2, int variant2, int rarity2,
        string card3, int variant3, int rarity3)
    {
        GameObject host = new("LethalCardsLocalBoosterReveal");
        BoosterRevealController controller = host.AddComponent<BoosterRevealController>();
        controller.Initialize(type, isGodPack,
            new[] { card1, card2, card3 },
            new[] { variant1, variant2, variant3 },
            new[] { rarity1, rarity2, rarity3 });
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

    private void Initialize(BoosterType type, bool isGodPack, string[] cardIds, int[] variants, int[] rarityValues)
    {
        Camera presentationCamera = Camera.main;
        if (presentationCamera == null)
        {
            Plugin.Log.LogWarning("BOOSTER REVEAL SKIPPED | No local presentation camera");
            Destroy(gameObject);
            return;
        }

        transform.position = presentationCamera.transform.position;
        transform.rotation = presentationCamera.transform.rotation;
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
            Destroy(gameObject);
            return;
        }
        StartCoroutine(RevealPack());
    }

    private void CreateAudioSources()
    {
        revealAudio = gameObject.AddComponent<AudioSource>();
        revealAudio.spatialBlend = 0f;
        anticipationAudio = gameObject.AddComponent<AudioSource>();
        anticipationAudio.spatialBlend = 0f;
    }

    private void CreateEffects()
    {
        if (BoosterRevealAssets.RevealFxPrefab == null) return;
        fxRoot = Instantiate(BoosterRevealAssets.RevealFxPrefab);
        fxRoot.name = "LethalCardsLocalRevealFX";
        fxRoot.transform.SetParent(transform, true);
        fxRoot.transform.position = ViewPoint(0f, 0f, 1.15f);
        fxRoot.transform.rotation = transform.rotation;
        presentationObjects.Add(fxRoot);
        foreach (ParticleSystem particles in fxRoot.GetComponentsInChildren<ParticleSystem>(true))
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (string expected in new[] { "HitSparkles", "UltraRevealBurst", "SecretRevealBurst",
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
        pack.position = ViewPoint(0f, -0.42f, 1.05f);
        pack.rotation = transform.rotation;
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
        yield return Move(cards[0], cards[0].position, ViewPoint(-0.34f, 0f, 1.15f), MoveAsideDuration);
        yield return new WaitForSeconds(BetweenCardsDelay);
        yield return RevealCard(1, false);
        yield return Move(cards[1], cards[1].position, ViewPoint(0.34f, 0f, 1.15f), MoveAsideDuration);
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

        Vector3 start = ViewPoint(0f, -0.42f, 1.05f);
        card.gameObject.SetActive(true);
        card.position = start;
        card.rotation = transform.rotation * Quaternion.Euler(0f, 180f, 0f);
        yield return Move(card, start, ViewPoint(0f, 0.12f, 1.15f), pull);
        yield return Move(card, card.position, ViewPoint(0f, 0f, 1.15f), DisplayMoveDuration);
        yield return new WaitForSeconds(pause);
        StartCoroutine(PlayRevealAudioDuringFlip(rarity, flip));
        yield return Flip(card, flip);
        PlayImpact(rarity);
        if (ultra || secret) StartCoroutine(ScalePunch(card, secret ? 1.12f : 1.08f));
        yield return new WaitForSeconds(RevealHoldDuration);
    }

    private IEnumerator BigHitBuildUp(CardRarity rarity, bool finalCard)
    {
        bool secret = rarity == CardRarity.SecretRare;
        float duration = BigHitBuildUpDuration * (secret ? 1.35f : 1f) * (finalCard ? 1.15f : 1f);
        float strength = (secret ? 0.005f : 0.0025f) * (finalCard ? 1.15f : 1f);
        ParticleSystem? sparkles = FindParticles("HitSparkles");
        if (sparkles != null)
        {
            sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var emission = sparkles.emission;
            emission.rateOverTime = 60f * (finalCard ? 1.15f : 1f);
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

    private Vector3 ViewPoint(float x, float y, float distance) =>
        transform.TransformPoint(new Vector3(x, y, distance));

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
        Vector3 pivotWorldStartPosition = tearStripPivot.position;
        Quaternion pivotWorldStartRotation = tearStripPivot.rotation;
        tearStripPivot.SetParent(transform, true);
        tearStripPivot.position = pivotWorldStartPosition;
        tearStripPivot.rotation = pivotWorldStartRotation;

        Quaternion pivotStartRotation = tearStripPivot.localRotation;
        Quaternion pivotPeeledRotation = pivotStartRotation * Quaternion.Euler(-65f, 0f, 0f);
        Vector3 pivotStartPosition = tearStripPivot.position;
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
        Vector3 flingStartPosition = tearStripPivot.position;
        Quaternion flingStartRotation = tearStripPivot.localRotation;
        Vector3 flingEndPosition = pivotStartPosition + transform.TransformVector(new Vector3(-0.85f, 0.55f, 0.12f));
        Quaternion flingEndRotation = pivotPeeledRotation * Quaternion.Euler(-25f, 0f, 120f);
        elapsed = 0f;
        while (elapsed < flingDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / flingDuration);
            tearStripPivot.position = Vector3.Lerp(flingStartPosition, flingEndPosition, t);
            tearStripPivot.localRotation = Quaternion.Slerp(flingStartRotation, flingEndRotation, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        tearStripPivot.position = flingEndPosition;
        tearStripPivot.localRotation = flingEndRotation;
        tearStripPivot.gameObject.SetActive(false);
    }

    private static IEnumerator Move(Transform target, Vector3 start, Vector3 end, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            target.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            elapsed += Time.deltaTime;
            yield return null;
        }
        target.position = end;
    }

    private static IEnumerator Flip(Transform target, float duration)
    {
        Quaternion start = target.rotation;
        Quaternion end = start * Quaternion.Euler(0f, 180f, 0f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            target.rotation = Quaternion.Slerp(start, end, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            elapsed += Time.deltaTime;
            yield return null;
        }
        target.rotation = end;
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
    }
}
