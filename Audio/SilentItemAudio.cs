using UnityEngine;

namespace LethalCards.Audio;

/// <summary>
/// Vanilla calls some Item sound fields without null checks, most notably when
/// pocketing or switching slots. One shared silent clip keeps intentionally
/// silent Lethal Cards items compatible without removing their AudioSources.
/// </summary>
internal static class SilentItemAudio
{
    private const int SampleRate = 44100;
    private static AudioClip? silentClip;

    internal static int AssignmentCount { get; private set; }

    internal static void EnsureFallbacks(Item? item)
    {
        if (item == null)
            return;

        AudioClip clip = GetOrCreateClip();
        if (item.grabSFX == null)
        {
            item.grabSFX = clip;
            AssignmentCount++;
        }
        if (item.dropSFX == null)
        {
            item.dropSFX = clip;
            AssignmentCount++;
        }
        if (item.pocketSFX == null)
        {
            item.pocketSFX = clip;
            AssignmentCount++;
        }
    }

    private static AudioClip GetOrCreateClip()
    {
        if (silentClip != null)
            return silentClip;

        silentClip = AudioClip.Create(
            "LethalCards_SilentItemSFX",
            1,
            1,
            SampleRate,
            false);
        silentClip.hideFlags = HideFlags.HideAndDontSave;
        return silentClip;
    }
}
