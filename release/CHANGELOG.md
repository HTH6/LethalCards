# Changelog

## v0.1.3 - Playtesting Release

This entry summarizes the current playtest build and development changes since the initial beta.

### Cards and Booster Balance

- Connected all 33 Set 1 definitions to their Unity Item assets through the existing asset-gated registration path.
- Assigned the remaining 26 base values while preserving CardIds, set numbers, rarities, and the original seven values.
- Expanded eligible pools to 10 Common, 9 Uncommon, 7 Rare, 4 Ultra Rare, and 3 Secret Rare cards when all assets load.
- Added vanilla inspection and per-instance variant-aware scan names without renaming shared Item assets.
- Added separate rarity tables for all three Light and normal Heavy slots. Normal Heavy packs no longer guarantee Rare-or-better.
- Increased Heavy God Pack chance to 5%; each of its three cards still rolls 60% Ultra Rare / 40% Secret Rare.
- Updated variant odds to Standard 69%, Foil 25%, Alternate Art 5%, and Misprint 1% across all packs.

### Booster Boxes, Controls, and Configuration

- Added Standard and Golden Booster Boxes as naturally spawning scrap.
- Standard boxes contain four sealed packs with independent 90% Light / 10% Heavy rolls. Golden boxes contain four sealed Heavy packs.
- Reused prepared booster prefabs, server-side spawning, safe inventory consumption, and duplicate-opening protection. Boxes do not generate cards directly.
- Corrected pack and box holder lookup to use the actual server-side holder rather than the last player slot matching a client ID.
- Added prepared-prefab/component checks and focused opening diagnostics.
- Added binding-aware Rip Pack / Open Box tooltips while retaining vanilla Drop prompts.
- Moved settings to LethalCards.cfg with migration from the legacy config when the new file is absent.
- Current spawn-weight defaults: Light 30, Heavy 15, Standard Box 10, Golden Box 5. Existing configured values take precedence.

### Collection and Grading Terminal

- Added sequential three-card collection pages, bounded navigation, and explicit page selection.
- Added contextual next / prev / previous commands that clear when leaving collection browsing.
- Added normalized card-name lookup, per-card variant discovery, collection totals, and cards help.
- Added exact grading / grades routes before vanilla keyword matching to avoid the Gratar prefix conflict.
- Added per-job Processing Order..., Assessing Grade..., Shipping Order..., and Ready for pickup! statuses for days 0, 1, 2, and 3+.
- Added read-only, targeted remote grading status with request IDs and cancellation of obsolete terminal responses.
- Added variant-aware grading names using the physical-card name formatter.
- Ready results show stored grades and final scrap values; day 0-2 responses transmit neither result.
- Added final-value persistence, backward-compatible loading of older jobs, and propagation to returned physical cards.
- Ready jobs remain active until confirmed pedestal pickup; collected jobs disappear from subsequent status requests.
- Grading remains $10 with a three-day turnaround. Grading formulas, collection discovery, and per-save isolation remain intact.

### Playtest Limitations and Roadmap

- Temporary host-only ship spawning of one Standard and one Golden box remains active.
- V1 needs pedestal redesigns and a fix for floating returned cards.
- V2 plans: ship collection display case, booster opening animation, and more elaborate variant artwork.
- V3 plans: upgrade shop and improved rotatable inspection for cards, packs, and boxes.
- This remains a playtesting build requiring further multiplayer, persistence, and value-synchronization testing.

## v0.1.0

### Collection and Terminal Updates

* Expanded `collection` to show discovered variants for each registered card, with card and variant discovery totals.
* Added `collection <card name>` for individual card details, set numbers, and variant discovery status.
* Added case-insensitive card lookup with support for formatting differences such as `coilhead`, `coil-head`, and `coil head`.
* Added `cards help` with the supported collection commands and helpful responses for unknown card names or `cards` subcommands.
* Collection views reuse existing discovery data without unlocking cards or variants. Discovery remains recorded after physical cards are sold, graded, lost, or dropped.

### Card Roster Updates

* Added a centralized 33-card roster with separate set numbers, stable card IDs, rarity, and asset availability metadata.
* Preserved the seven existing cards' save IDs and base values for collection and grading save compatibility.
* Collection screens now include the full roster and identify cards that are not yet available in packs.
* Booster selection only includes implemented cards with successfully loaded assets. The additional 26 roster entries do not add playable card assets yet.
* Added validation for duplicate card IDs, set numbers, normalized names, and invalid card metadata.

### Gameplay and Configuration Updates

* Added BepInEx spawn-weight settings under `Spawn Weights`: `LightBoosterWeight` defaults to 12 and `HeavyBoosterWeight` defaults to 4.
* Setting a booster spawn weight to zero disables its natural spawning while preserving item registration for saves. Restart the game after changing these settings.
* Disabled natural loose-card spawning; cards are obtained through booster packs.
* Disabled automatic ship booster spawns, inflated test spawn weights, and development input/position logging. Testing code is retained for future development.
* Disabled instant, free, and fixed-result grading overrides. Grading now uses the existing random grade logic, costs $10, and takes three days.

### Multiplayer Updates

* Added server-controlled item consumption and inventory cleanup for booster opening and grading submissions.
* Strengthened server validation of booster holders and grading submissions.
* Improved collection synchronization for joining clients and cleanup between sessions and save slots.
* Synchronized returned graded-card display positions and server-confirmed pickup handling while retaining grading jobs until pickup.
* Improved physical card value synchronization and added build-time networking verification tooling.

### Initial Beta Release

* Added the initial Lethal Cards trading card system.
* Added seven collectible cards:

  * Hoarding Bug
  * Eyeless Dog
  * Snare Flea
  * Bracken
  * Coil-Head
  * Jester
  * Ghost Girl
* Added unique card IDs and card rarity levels.
* Added Light Booster Packs.
* Added Heavy Booster Packs.
* Added three-card booster opening.
* Added Heavy Booster Pack God Pack chance.
* Added Standard, Foil, Alternate-Art, and Misprint variants.
* Added variant-based card value multipliers.
* Added persistent collection tracking.
* Added per-save collection persistence.
* Added card grading.
* Added grading jobs and grading persistence.
* Added graded card value multipliers.
* Added grading submission pedestal at the Company.
* Added graded-card return pickup pedestal.
* Added persistent returned grading jobs until the returned card is picked up.
* Added duplicate-return protection using grading Job IDs.
* Added fixed pedestal display positioning for returned graded cards.
* Added support for displaying multiple returned graded cards across the pickup pedestal.
* Added save identity isolation between Lethal Company save slots.
* Added multiplayer networking infrastructure for cards, boosters, collection data, and grading.

### Beta Notes

* Multiplayer behavior is still undergoing testing.
* A reported booster handoff issue remains under investigation: the host may be unable to open a booster previously handled by a remote client.
* Recent roster, terminal, and configuration updates still need in-game multiplayer verification.
* Card values, rarity rates, booster odds, and grading multipliers are subject to change.
* Variant-specific artwork and presentation effects are not yet complete.
* Booster Bundles and Booster Boxes are planned but not included in this release.
