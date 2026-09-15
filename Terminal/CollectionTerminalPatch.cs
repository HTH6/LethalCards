using System.Text;
using System;
using System.Linq;
using HarmonyLib;
using LethalCards.Cards;

namespace LethalCards.TerminalCommands;

[HarmonyPatch(typeof(Terminal))]
public static class CollectionTerminalPatch
{
    private const string CollectionCommand = "collection";
    private const string CardsCommand = "cards";
    private const string HelpSubcommand = "help";

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

        // Previously only: if (input != "collection")
        // const string command = "collection";
        const string command = CollectionCommand;
        // bool showHelp = input == "cards help";
        bool isCardsCommand = input == CardsCommand ||
            (input.StartsWith(CardsCommand, StringComparison.Ordinal) &&
             input.Length > CardsCommand.Length && char.IsWhiteSpace(input[CardsCommand.Length]));
        bool showHelp = isCardsCommand &&
            input.Substring(CardsCommand.Length).Trim() == HelpSubcommand;
        // Previous check handled only collection commands.
        // Previously only recognized the exact cards help route.
        if (!isCardsCommand && input != command &&
            !(input.StartsWith(command, StringComparison.Ordinal) &&
              input.Length > command.Length && char.IsWhiteSpace(input[command.Length])))
            return true;

        // string cardQuery = input.Substring(command.Length).Trim();
        // string cardQuery = showHelp ? "" : input.Substring(command.Length).Trim();
        string cardQuery = isCardsCommand ? "" : input.Substring(command.Length).Trim();

        Plugin.Log.LogInfo(
            // "TERMINAL: COLLECTION command received."
            $"TERMINAL: LETHAL CARDS command received | Command={(isCardsCommand ? CardsCommand : command)}"
        );

        TerminalNode node =
            UnityEngine.ScriptableObject.CreateInstance<TerminalNode>();

        node.clearPreviousText = true;
        // node.displayText = BuildCollectionText();
        // node.displayText = cardQuery.Length == 0 ? BuildCollectionText() : BuildCardDetailText(cardQuery);
        // Previous inline help retained for reference:
        // "\n=== LETHAL CARDS COMMANDS ===\n\ncollection\n  List all cards and discovered variants.\n\ncollection <card name>\n  Show discovery details for one card.\n  Example: collection ghost girl\n\ncards help\n  Show this command list.\n\n"
        node.displayText = isCardsCommand
            ? (showHelp ? BuildHelpText() :
                $"\nUnknown Lethal Cards command.\nType \"{CardsCommand} {HelpSubcommand}\" for available commands.\n\n")
            : cardQuery.Length == 0
            ? BuildCollectionText()
            : BuildCardDetailText(cardQuery);

        __result = node;

        // false = skip the game's normal command parser,
        // because we handled this command ourselves.
        return false;
    }

    private static string BuildHelpText()
    {
        // List only routes implemented by this parser. Grading has no terminal route yet.
        return "\n=== LETHAL CARDS COMMANDS ===\n\n" +
            $"{CollectionCommand}\nView your discovered cards and variants.\n\n" +
            $"{CollectionCommand} <card name>\nView detailed collection information for one card.\n\n" +
            $"{CardsCommand} {HelpSubcommand}\nDisplay this help screen.\n\n";
    }

    private static string NormalizeCardName(string value)
    {
        return CardDatabase.NormalizeName(value);
        /* Previous local normalizer retained; the registry now owns name matching.
        StringBuilder normalized = new();
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
                normalized.Append(char.ToUpperInvariant(character));
        }
        return normalized.ToString();
        */
    }

    private static string BuildCardDetailText(string query)
    {
        string normalizedQuery = NormalizeCardName(query);
        CardDefinition? match = null;
        foreach (CardDefinition card in CardDatabase.Cards)
        {
            if (normalizedQuery.Length == 0 ||
                NormalizeCardName(card.DisplayName) != normalizedQuery)
                continue;

            if (match != null)
                return "\nMore than one card matches that name.\nUse \"collection\" to view all cards.\n\n";
            match = card;
        }

        if (match == null)
            return "\nCard not found.\n\nUse \"collection\" to view all cards.\nExample: collection ghost girl\n\n";

        /* Persistent ID suffixes no longer necessarily match display set numbers.
        string setPrefix = match.SetId + "-";
        string setNumber = match.CardId.StartsWith(setPrefix, StringComparison.OrdinalIgnoreCase)
            ? match.CardId.Substring(setPrefix.Length)
            : match.CardId;
        */
        string setNumber = match.SetNumber.ToString("D3");
        bool discovered = CollectionManager.HasCard(match.CardId);
        CardVariant[] variants = (CardVariant[])Enum.GetValues(typeof(CardVariant));
        int found = 0;
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine($"=== {match.DisplayName.ToUpperInvariant()} ===");
        builder.AppendLine();
        builder.AppendLine($"Set: {match.SetId}");
        builder.AppendLine($"Set #: {setNumber}");
        if (!match.IsImplemented)
            builder.AppendLine("Not yet available in packs.");
        builder.AppendLine($"Discovered: {(discovered ? "Yes" : "No")}");
        builder.AppendLine();
        builder.AppendLine("Variants:");
        foreach (CardVariant variant in variants)
        {
            bool hasVariant = CollectionManager.HasVariant(match.CardId, variant);
            if (hasVariant)
                found++;
            string label = System.Text.RegularExpressions.Regex.Replace(
                variant.ToString(), "([a-z])([A-Z])", "$1 $2");
            builder.AppendLine($"[{(hasVariant ? "X" : " ")}] {label}");
        }
        builder.AppendLine();
        builder.AppendLine($"Variants Found: {found} / {variants.Length}");
        builder.AppendLine();
        return builder.ToString();
    }

    private static string BuildCollectionText()
    {
        StringBuilder builder = new();
        CardVariant[] variants = (CardVariant[])Enum.GetValues(typeof(CardVariant));
        int cardsFound = 0;
        int variantsFound = 0;

        builder.AppendLine();
        builder.AppendLine("=== LETHAL CARDS COLLECTION ===");
        builder.AppendLine();

        // Previously sorted by persistent ID; display numbers are now separate.
        foreach (CardDefinition card in CardDatabase.Cards.OrderBy(card => card.SetId, StringComparer.Ordinal).ThenBy(card => card.SetNumber))
        {
            if (CollectionManager.HasCard(card.CardId))
                cardsFound++;

            // Keep the set prefix so numbers remain unambiguous across sets.
            // builder.AppendLine($"{card.CardId} - {card.DisplayName}");
            builder.AppendLine($"{card.SetId}-{card.SetNumber:D3} - {card.DisplayName}");
            if (!card.IsImplemented)
                builder.AppendLine("  Not yet available in packs.");
            foreach (CardVariant variant in variants)
            {
                bool found = CollectionManager.HasVariant(card.CardId, variant);
                if (found)
                    variantsFound++;
                string label = System.Text.RegularExpressions.Regex.Replace(
                    variant.ToString(), "([a-z])([A-Z])", "$1 $2");
                builder.AppendLine($"  [{(found ? "X" : " ")}] {label}");
            }
            builder.AppendLine();
        }

        builder.AppendLine($"Cards Discovered: {cardsFound} / {CardDatabase.Cards.Count}");
        builder.AppendLine($"Variants Discovered: {variantsFound} / {CardDatabase.Cards.Count * variants.Length}");
        builder.AppendLine();
        builder.AppendLine("Type \"collection <card name>\" for details.");
        builder.AppendLine("Type \"cards help\" for Lethal Cards commands.");
        builder.AppendLine();
        return builder.ToString();
    }

    /* Previous compact collection display retained for reference.
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
    */
}
