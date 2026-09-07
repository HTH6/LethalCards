using UnityEngine;

namespace LethalCards.Boosters;

public class BoosterPackItem : PhysicsProp
{
    public override void ItemActivate(bool used, bool buttonDown = true)
    {
        base.ItemActivate(used, buttonDown);

        if (!buttonDown)
            return;

        Plugin.Log.LogInfo("BOOSTER PACK USED");

        // We'll add card spawning here next.
    }
}