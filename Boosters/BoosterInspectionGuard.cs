using GameNetcodeStuff;

namespace LethalCards.Boosters;

internal static class BoosterInspectionGuard
{
    internal static bool IsInspectingThis(PlayerControllerB? player, GrabbableObject item)
    {
        return player != null &&
            player.IsInspectingItem &&
            (ReferenceEquals(player.currentlyHeldObject, item) ||
             ReferenceEquals(player.currentlyHeldObjectServer, item));
    }
}
