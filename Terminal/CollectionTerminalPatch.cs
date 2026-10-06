using System.Text;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LethalCards.Cards;
using LethalCards.Grading;
using Unity.Netcode;

namespace LethalCards.TerminalCommands;

[HarmonyPatch(typeof(Terminal))]
public static class CollectionTerminalPatch
{
    private const string CollectionCommand = "collection";
    private const string CardsCommand = "cards";
    private const string HelpSubcommand = "help";
    private const int CollectionPageSize = 3;
    // Navigation belongs to this terminal instance, not the shared collection/save state.
    private sealed class CollectionPageState
    {
        public int PageIndex;
        public bool Browsing;
    }
    private static ConditionalWeakTable<Terminal, CollectionPageState> CollectionPages = new();

    internal static void ResetContext() => CollectionPages = new();

    private static void SetCollectionContext(Terminal terminal, bool browsing)
    {
        GradingNetworkSync.CancelStatusRequest();
        CollectionPageState state = CollectionPages.GetValue(terminal, _ => new CollectionPageState());
        if (state.Browsing == browsing)
            return;
        state.Browsing = browsing;
        // Plugin.Log.LogDebug(browsing ? "COLLECTION CONTEXT ENTER" : "COLLECTION CONTEXT EXIT");
    }

    [HarmonyPatch("QuitTerminal"), HarmonyPrefix]
    private static void QuitPrefix(Terminal __instance) => SetCollectionContext(__instance, false);

    [HarmonyPatch("BeginUsingTerminal"), HarmonyPrefix]
    private static void BeginPrefix(Terminal __instance) => SetCollectionContext(__instance, false);

    [HarmonyPatch("OnDisable"), HarmonyPrefix]
    private static void DisablePrefix(Terminal __instance) => SetCollectionContext(__instance, false);

    [HarmonyPatch("ParsePlayerSentence")]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool ParsePlayerSentencePrefix(
        Terminal __instance,
        ref TerminalNode __result)
    {
        if (__instance == null)
            return true;

        GradingNetworkSync.CancelStatusRequest();

        string input =
            __instance.screenText.text
                .Substring(
                    __instance.screenText.text.Length -
                    __instance.textAdded
                )
                .Trim()
                .ToLowerInvariant();

        CollectionPageState context = CollectionPages.GetValue(__instance, _ => new CollectionPageState());
        if (context.Browsing && (input == "next" || input == "prev" || input == "previous"))
            input = CollectionCommand + " " + (input == "next" ? "next" : "previous");
        if (input == "grading" || input == "grades")
        {
            NetworkManager? manager = NetworkManager.Singleton;
            Plugin.Log.LogInfo(
                $"GRADING COMMAND | Command={input} | IsHost={manager?.IsHost ?? false} | " +
                $"IsServer={manager?.IsServer ?? false} | IsClient={manager?.IsClient ?? false} | " +
                $"LocalClientId={(manager != null ? manager.LocalClientId.ToString() : "<none>")} | " +
                $"RuntimeJobCount={GradingManager.Jobs.Count}");
            SetCollectionContext(__instance, false);
            TerminalNode gradingNode = UnityEngine.ScriptableObject.CreateInstance<TerminalNode>();
            gradingNode.clearPreviousText = true;
            if (manager != null && !manager.IsServer)
            {
                gradingNode.displayText = "\nLETHAL CARDS GRADING STATUS\n\nRetrieving grading status...\n\n";
                if (!GradingNetworkSync.RequestStatus((jobCount, display) =>
                {
                    if (__instance == null)
                    {
                        Plugin.Log.LogWarning("GRADING RESPONSE BLOCKED | Reason=TerminalDestroyedBeforeDisplay");
                        return;
                    }
                    if (!__instance.terminalInUse)
                    {
                        Plugin.Log.LogWarning("GRADING RESPONSE BLOCKED | Reason=TerminalNoLongerInUse");
                        return;
                    }
                    TerminalNode response = UnityEngine.ScriptableObject.CreateInstance<TerminalNode>();
                    response.clearPreviousText = true;
                    response.displayText = display;
                    Plugin.Log.LogInfo(
                        $"GRADING CLIENT DISPLAY | JobCount={jobCount} | OutputLength={display?.Length ?? 0}");
                    __instance.LoadNewNode(response);
                }))
                    gradingNode.displayText = "\nLETHAL CARDS GRADING STATUS\n\nUnable to contact the server. Try grades again.\n\n";
            }
            else
                gradingNode.displayText = BuildGradingText(input);
            __result = gradingNode;
            return false;
        }

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
        {
            SetCollectionContext(__instance, false);
            return true;
        }

        SetCollectionContext(__instance, !isCardsCommand);

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
            : BuildCollectionResponse(__instance, cardQuery);

        __result = node;

        // false = skip the game's normal command parser,
        // because we handled this command ourselves.
        return false;
    }

    private static string BuildHelpText()
    {
        // List only routes implemented by this parser.
        return "\n=== LETHAL CARDS COMMANDS ===\n\n" +
            $"{CollectionCommand}\nView your discovered cards and variants.\n\n" +
            $"{CollectionCommand} next / previous\nMove between collection pages.\n\n" +
            $"{CollectionCommand} page <number>\nView a specific collection page.\n\n" +
            $"{CollectionCommand} <card name>\nView detailed collection information for one card.\n\n" +
            "next / prev / previous\nNavigate while browsing collection.\n\n" +
            "grading / grades\nView grading status and ready results.\n\n" +
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

    private static string BuildCollectionResponse(Terminal terminal, string query)
    {
        CollectionPageState state = CollectionPages.GetValue(terminal, _ => new CollectionPageState());
        if (query.Length == 0)
            state.PageIndex = 0;
        else if (query == "next")
            state.PageIndex++;
        else if (query == "previous" || query == "prev")
            state.PageIndex--;
        else if (query == "page" || (query.StartsWith("page", StringComparison.Ordinal) &&
                 query.Length > 4 && char.IsWhiteSpace(query[4])))
        {
            if (!int.TryParse(query.Substring(4).Trim(), out int page) || page < 1)
                return "\nUse \"collection page <number>\" with a positive page number.\n\n";
            state.PageIndex = page - 1;
        }
        else
            return BuildCardDetailText(query);

        int pageCount = Math.Max(1, (CardDatabase.Cards.Count + CollectionPageSize - 1) / CollectionPageSize);
        state.PageIndex = Math.Max(0, Math.Min(state.PageIndex, pageCount - 1));
        return BuildCollectionText(state.PageIndex);
    }

    private static string BuildCollectionText(int pageIndex)
    {
        StringBuilder builder = new();
        CardVariant[] variants = (CardVariant[])Enum.GetValues(typeof(CardVariant));
        int cardsFound = 0;
        int variantsFound = 0;
        CardDefinition[] entries = CardDatabase.Cards
            .OrderBy(card => card.SetId, StringComparer.Ordinal)
            .ThenBy(card => card.SetNumber).ToArray();
        int pageCount = Math.Max(1, (entries.Length + CollectionPageSize - 1) / CollectionPageSize);
        pageIndex = Math.Max(0, Math.Min(pageIndex, pageCount - 1));
        int startIndex = pageIndex * CollectionPageSize;
        int endIndex = Math.Min(startIndex + CollectionPageSize, entries.Length);
        // Plugin.Log.LogDebug($"COLLECTION PAGE | Page={pageIndex + 1}/{pageCount} | Start={(entries.Length == 0 ? 0 : startIndex + 1)} | End={endIndex} | Total={entries.Length} | Range=1-based inclusive");

        // Totals describe the full collection, independently of the visible page.
        foreach (CardDefinition card in entries)
        {
            if (CollectionManager.HasCard(card.CardId))
                cardsFound++;
            variantsFound += variants.Count(variant => CollectionManager.HasVariant(card.CardId, variant));
        }

        builder.AppendLine();
        builder.AppendLine("=== LETHAL CARDS COLLECTION ===");
        builder.AppendLine($"Page {pageIndex + 1} / {pageCount}");
        builder.AppendLine();

        // Previously sorted by persistent ID; display numbers are now separate.
        for (int index = startIndex; index < endIndex; index++)
        {
            CardDefinition card = entries[index];

            // Keep the set prefix so numbers remain unambiguous across sets.
            // builder.AppendLine($"{card.CardId} - {card.DisplayName}");
            builder.AppendLine($"{card.SetId}-{card.SetNumber:D3} - {card.DisplayName}");
            if (!card.IsImplemented)
                builder.AppendLine("  Not yet available in packs.");
            foreach (CardVariant variant in variants)
            {
                bool found = CollectionManager.HasVariant(card.CardId, variant);
                string label = System.Text.RegularExpressions.Regex.Replace(
                    variant.ToString(), "([a-z])([A-Z])", "$1 $2");
                builder.AppendLine($"  [{(found ? "X" : " ")}] {label}");
            }
            builder.AppendLine();
        }

        builder.AppendLine($"Cards Discovered: {cardsFound} / {CardDatabase.Cards.Count}");
        builder.AppendLine($"Variants Discovered: {variantsFound} / {CardDatabase.Cards.Count * variants.Length}");
        if (pageIndex > 0)
            builder.AppendLine("Previous: collection previous");
        if (pageIndex + 1 < pageCount)
            builder.AppendLine("Next: collection next");
        builder.AppendLine();
        builder.AppendLine("Type \"collection <card name>\" for details.");
        builder.AppendLine("While browsing: next / prev / previous.");
        builder.AppendLine("Type \"cards help\" for Lethal Cards commands.");
        builder.AppendLine();
        return builder.ToString();
    }

    internal static string BuildGradingText(string command)
    {
        // Called on the host only, for both local commands and targeted remote responses.
        StringBuilder builder = new("\nLETHAL CARDS GRADING STATUS\n\n");
        int currentDay = GradingDayManager.CurrentDay;
        // Plugin.Log.LogInfo($"GRADING TERMINAL COMMAND | Command={command} | Jobs={GradingManager.Jobs.Count}");
        if (GradingManager.Jobs.Count == 0)
            builder.AppendLine("No cards are currently in grading.");
        foreach (GradingJob job in GradingManager.Jobs)
        {
            long elapsed = (long)currentDay - job.SubmittedDay;
            string status = elapsed <= 0 ? "Processing Order..." : elapsed == 1 ? "Assessing Grade..." :
                elapsed == 2 ? "Shipping Order..." : "Ready for pickup!";
            bool visible = elapsed >= 3;
            string displayName = CardNameFormatter.GetDisplayName(
                CardDatabase.GetById(job.CardId)?.DisplayName ?? job.CardId, job.Variant);
            builder.AppendLine(displayName);
            builder.AppendLine($"Status: {status}");
            if (visible)
            {
                builder.AppendLine($"Grade: {job.Grade}");
                builder.AppendLine(job.FinalValue.HasValue ? $"Value: ${job.FinalValue.Value}" : "Value: unavailable");
            }
            builder.AppendLine();
            Plugin.Log.LogDebug($"GRADING STATUS | CardId={job.CardId} | Variant={job.Variant} | DisplayName=\"{displayName}\" | SubmittedDay={job.SubmittedDay} | " +
                $"CurrentDay={currentDay} | DaysElapsed={elapsed} | Status=\"{status}\" | GradeVisible={visible}" +
                $" | ValueVisible={visible}" + (visible ? $" | Grade={job.Grade} | FinalValue={job.FinalValue}" : ""));
        }
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
