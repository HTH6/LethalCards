using System.Text;
using HarmonyLib;
using LethalCards.Cards;

namespace LethalCards.TerminalCommands;

[HarmonyPatch(typeof(Terminal))]
public static class CollectionTerminalPatch
{
    [HarmonyPatch("ParsePlayerSentence")]
    [HarmonyPrefix]
    private static bool ParsePlayerSentencePrefix(
        Terminal __instance,
        ref TerminalNode __result)
    {
        if (__instance == null)
            return true;

        string input =
            __instance.screenText.text
                .Substring(
                    __instance.screenText.text.Length -
                    __instance.textAdded
                )
                .Trim()
                .ToLowerInvariant();

        if (input != "collection")
            return true;

        Plugin.Log.LogInfo(
            "TERMINAL: COLLECTION command received."
        );

        TerminalNode node =
            UnityEngine.ScriptableObject.CreateInstance<TerminalNode>();

        node.clearPreviousText = true;
        node.displayText = BuildCollectionText();

        __result = node;

        // false = skip the game's normal command parser,
        // because we handled this command ourselves.
        return false;
    }

    private static string BuildCollectionText()
    {
        StringBuilder builder = new();

        builder.AppendLine();
        builder.AppendLine("LETHAL CARDS COLLECTION");
        builder.AppendLine();
        builder.AppendLine("CORE SET LC01");
        builder.AppendLine();

        builder.AppendLine(
            $"{CollectionManager.DiscoveredCardCount} / " +
            $"{CollectionManager.TotalCardCount} cards discovered"
        );

        builder.AppendLine();

        foreach (CardDefinition card in CardDatabase.Cards)
        {
            bool discovered =
                CollectionManager.HasCard(card.CardId);

            string status =
                discovered
                    ? "FOUND"
                    : "-----";

            builder.AppendLine(
                $"{card.CardId}  " +
                $"{card.DisplayName}  " +
                $"{status}"
            );
        }

        builder.AppendLine();
        builder.AppendLine(
            $"Variants discovered: " +
            $"{CollectionManager.DiscoveredVariantCount}"
        );

        builder.AppendLine();

        return builder.ToString();
    }
}