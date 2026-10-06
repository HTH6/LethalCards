# Changelog

## 1.0.1

- Added YouTube trailer/showcase link.
- Added Thunderstore page link to README.

## v0.1.3 - Playtesting Release

This entry summarizes the current playtest build and development changes since the initial beta.

### Cards and Booster Balance

- Connected all 33 Set 1 definitions to their Unity Item assets through the existing asset-gated registration path.
- Assigned the remaining 26 base values while preserving CardIds, set numbers, rarities, and the original seven values.
- Expanded eligible pools to 10 Common, 9 Uncommon, 7 Rare, 4 Ultra Rare, and 3 Secret Rare cards when all assets load.
- Added vanilla inspection and per-instance variant-aware scan names without renaming shared Item assets.
- Added separate rarity tables for all three Light and normal Heavy pack slots. Normal Heavy packs no longer guarantee Rare-or-better.
- Light Booster Pack rarity tables:
  - Slot 1: 72.5% Common / 20% Uncommon / 7.5% Rare.
  - Slot 2: 69% Common / 20% Uncommon / 7.5% Rare / 2.5% Ultra Rare / 1% Secret Rare.
  - Slot 3: 58% Common / 20% Uncommon / 15% Rare / 5% Ultra Rare / 2% Secret Rare.
- Heavy Booster Pack rarity tables:
  - Slot 1: 30% Common / 55% Uncommon / 10% Rare / 5% Ultra Rare.
  - Slot 2: 25% Common / 50% Uncommon / 15% Rare / 10% Ultra Rare.
  - Slot 3: 20% Common / 40% Uncommon / 20% Rare / 15% Ultra Rare / 5% Secret Rare.
- Increased Heavy God Pack chance to 5%; each of its three cards rolls 60% Ultra Rare / 40% Secret Rare.
- Updated variant odds to Standard 69%, Foil 25%, Alternate Art 5%, and Misprint 1% across all packs.
- Preserved server-authoritative card generation so clients do not independently roll rarity or variant results.

### Booster Boxes, Controls, and Configuration

- Added Standard and Golden Booster Boxes as naturally spawning scrap.
- Standard Booster Boxes contain four sealed packs with independent 90% Light / 10% Heavy rolls.
- Golden Booster Boxes contain four guaranteed Heavy Booster Packs.
- Reused prepared booster prefabs, server-side spawning, safe inventory consumption, and duplicate-opening protection. Boxes do not generate cards directly.
- Corrected pack and box holder lookup to use the actual server-side holder rather than the last player slot matching a client ID.
- Added prepared-prefab/component checks and focused opening diagnostics.
- Added binding-aware `Rip Pack` / `Open Box` tooltips while retaining vanilla Drop prompts.
- Enabled vanilla inspection support for cards, booster packs, and booster boxes.
- Moved settings to `LethalCards.cfg` with migration from the legacy config when the new file is absent.
- Removed normal reliance on development-only ship spawning for booster boxes; natural scrap spawn weights are now used for gameplay.
- Retained old debug-spawn code in commented form so it can be restored easily for future development testing.
- Current playtest spawn configuration has been increased to:
  - Light Booster Pack: 45
  - Heavy Booster Pack: 25
  - Standard Booster Box: 20
  - Golden Booster Box: 10
- Existing BepInEx configuration values continue to take precedence over code defaults.

### Booster Opening Presentation

- Added a full three-card animated booster opening presentation.
- Booster reveals now use local visual-only copies while leaving the real networked cards untouched.
- Added an animated wrapper tear-strip sequence before cards are revealed.
- Added sequential card pulls, center presentation, card flips, and side positioning for the first two cards.
- Added separate reveal timing for Common/Uncommon, Rare, Ultra Rare, and Secret Rare cards.
- Added slower, more dramatic Ultra Rare and Secret Rare reveal timing.
- Added rarity-based card reveal audio.
- Added Ultra Rare and Secret Rare anticipation audio during the pre-reveal buildup.
- Added pack rumble during Ultra Rare and Secret Rare buildup sequences.
- Added buildup sparkle particles around the booster pack before large hits.
- Increased buildup sparkle density for improved in-game visibility.
- Added gold Ultra Rare sparkle presentation.
- Added Secret Rare multicolor/rainbow sparkle presentation.
- Added rarity-specific reveal bursts, flashes, lingering glow effects, and the Secret Rare rainbow ring.
- Added a brief scale-punch effect when Ultra Rare and Secret Rare cards are revealed.
- Added God Pack-specific reveal audio.
- Reveal effects are loaded from the `lethalcards` AssetBundle and instantiated as local cosmetic presentation only.
- Reveal card clones contain visual components only and do not duplicate network or gameplay components.
- Production reveal positioning is camera-relative instead of relying on prototype world-space coordinates.
- Added cleanup protections so interrupted/restarted reveals do not leave old audio, particles, presentation cards, or pack visuals behind.

### Card Variant Presentation

- Added persistent physical-card visuals for Standard, Foil, Alternate Art, and Misprint cards.
- Variant artwork is derived from the existing authoritative variant rather than rolling a second visual variant.
- Foil, Alternate Art, and Misprint presentation now persists on the actual physical card after a booster reveal finishes.
- Added support for normal HDRP/Lit `_BaseColorMap` card textures and Alternate Art Shader Graph `_BaseMap` textures.
- Variant-aware physical card names are preserved throughout collection, grading, and returned-card workflows.

### Card Pickup and World Placement

- Corrected player-held card positioning using an additive authored offset correction.
- Corrected held booster-pack positioning using the same player-view alignment approach.
- Tuned booster-box held positioning separately for their larger dimensions.
- Increased physical card size from the original undersized test presentation.
- Increased usable pickup/collider coverage to make cards easier to target and collect.
- Added inspection support without replacing normal pickup/drop behavior.
- Corrected custom-spawned cards and booster packs settling partially inside floors.
- Preserved vanilla `FallToGround` behavior and added a targeted one-time `+0.04f` world-space landing correction only for Lethal Cards custom spawns.
- The custom floor correction is idempotent and does not alter normal player-dropped objects.
- Final item vertical offsets:
  - Cards: `0.03f`
  - Light/Heavy Booster Packs: `0.06f`
  - Standard/Golden Booster Boxes: `0.15f`
- Existing player drop behavior remains untouched.

### Collection and Grading Terminal

- Added sequential three-card collection pages, bounded navigation, and explicit page selection.
- Added contextual `next`, `prev`, and `previous` commands that clear when leaving collection browsing.
- Added normalized card-name lookup, per-card variant discovery, collection totals, and `cards help`.
- Added exact `grading` / `grades` routes before vanilla keyword matching to avoid the Gratar prefix conflict.
- Added per-job `Processing Order...`, `Assessing Grade...`, `Shipping Order...`, and `Ready for pickup!` statuses for days 0, 1, 2, and 3+.
- Added read-only, targeted remote grading status with request IDs and cancellation of obsolete terminal responses.
- Added variant-aware grading names using the physical-card name formatter.
- Ready results show stored grades and final scrap values; day 0-2 responses transmit neither result.
- Added final-value persistence, backward-compatible loading of older jobs, and propagation to returned physical cards.
- Ready jobs remain active until confirmed pedestal pickup; collected jobs disappear from subsequent status requests.
- Grading remains $10 with a three-day turnaround.
- Grading formulas, collection discovery, and per-save isolation remain intact.

### Graded Card Slabs

- Added a reusable graded-card slab presentation for returned graded cards.
- The existing card remains the authoritative networked/grabbable object.
- The slab is attached as a visual-only child rather than spawning a second networked card.
- Added grading-label presentation containing:
  - Card name
  - Set number
  - Variant
  - Grade descriptor
  - Numerical grade
  - Barcode
  - Lethal Cards grading logo
- Added grade-specific label styling for normal grades, Grade 1, Grade 9, and Grade 10.
- Added grading descriptors:
  - 10 - GEM MINT
  - 9 - MINT
  - 8 - NM-MT
  - 7 - NM
  - 6 - EX-MT
  - 5 - EX
  - 4 - VG-EX
  - 3 - VG
  - 2 - GOOD
  - 1 - POOR
- Added Standard, Foil, Alternate Art, and Misprint variant text to the grading label.
- Expanded the original card collider to cover the slab instead of adding gameplay colliders to the presentation object.
- Suppressed the older floating grade presentation when the slab presentation succeeds.
- Slab construction is idempotent and preserves existing grading/save behavior.

### Grading Submission Pedestal

- Replaced the original grading submission pedestal presentation with the new `GradingSubmissionPedestal_Animated` prefab.
- Added a production `GradingPedestalAnimationController` inside the mod DLL.
- Added animated CRT-style idle display with:
  - blinking cursor
  - subtle emission flicker
  - periodic brightness glitch
  - synchronized text jitter
- Added green/red pedestal status indicators.
- Added a complete successful grading submission sequence:
  - submitted-card presentation
  - `SCANNING` display state
  - moving scan beam
  - tracking scan fan and spotlight
  - scanner audio
  - animated scanning dots
  - animated card-slot opening
  - card-drop animation
  - card-drop sound
  - slot closing
  - `SUBMISSION COMPLETE!` display
  - automatic return to idle
- Added authored scan and card-drop AudioSources directly to the grading pedestal prefab.
- Successful scanner presentation begins only after the existing grading system has accepted the grading job.
- The real authoritative card is still consumed using the existing server-side grading path.
- Scanner animation uses a visual-only copy of the submitted card instead of moving the networked `GrabbableObject`.
- The presentation copy preserves the submitted card's authoritative Standard/Foil/Alternate Art/Misprint variant.
- Successful grading presentation is synchronized to connected players while gameplay authority remains on the server.
- Added reentry protection and cleanup so overlapping interactions cannot leave stuck scanner effects, audio, temporary cards, or slot states behind.

### Insufficient Credits Feedback

- Added a dedicated insufficient-credit pedestal animation.
- When a player attempts grading without enough credits:
  - the grading order is rejected normally
  - no credits are deducted
  - no grading job is created
  - the real card is not consumed
  - the normal scan animation does not run
- The CRT screen temporarily changes to red and displays:

  `NOT ENOUGH`
  `CREDITS`

- The green status light turns off and the red status light turns on during the error.
- The pedestal automatically restores its original CRT emission and idle state afterward.
- Insufficient-credit presentation is sent only to the requesting player rather than unnecessarily broadcasting the failure to all clients.

### Grading Return Pedestal and Returned Cards

- Redesigned and improved the grading return pedestal presentation.
- Corrected returned graded cards previously falling/clipping through the pedestal surface.
- Returned cards now use a fixed three-column placement grid based on the authored `GradedCardRestAnchor`.
- Preserved the working return layout:
  - Column spacing: `0.55`
  - Row spacing: `-0.22`
- Returned cards preserve the authored rest-anchor rotation.
- Returned graded cards remain authoritative networked objects and are still protected from duplicate restoration through grading Job IDs.
- Grading return jobs are removed only when the returned card is successfully collected.

### Company Pedestal Initialization

- Separated grading pedestal initialization from returned-card restoration timing.
- Grading submission and return pedestals now appear as soon as the Company/Gordion scene and `DepositItemsDesk` are available instead of waiting for the ship's full landing-state transition.
- Removed the visually jarring multi-second delay before the grading pedestals appear.
- Returned graded cards now wait independently for the return pedestal to become fully ready.
- Added `IsReturnPedestalReady` checks requiring:
  - return pedestal instance exists
  - pedestal is active
  - `GradedCardRestAnchor` has been resolved
- Returned cards wait an additional short `0.25s` safety period after readiness before spawning.
- Readiness is checked again after the safety delay before any cards are restored.
- Slow-loading/heavily modded clients are no longer subject to a fixed restoration timeout.
- The server continues checking return-pedestal readiness for the entire valid Company visit.
- A five-second threshold now produces only one informational warning and does not abandon card restoration.
- Readiness is polled at `0.25s` intervals.
- Waiting is safely cancelled when leaving Gordion, beginning departure, unloading/resetting the session, disconnecting, or shutting down.
- Returned-card spawning remains server-authoritative.
- Duplicate prevention and existing return-grid placement remain unchanged.

### Multiplayer and Runtime Safety

- Booster opening presentation remains entirely cosmetic and local to each client.
- Real generated cards remain network-authoritative and are not moved or replaced for reveal animations.
- Grading presentation uses visual-only clones without:
  - `NetworkObject`
  - `NetworkBehaviour`
  - `PhysicsProp`
  - Rigidbody
  - gameplay colliders
  - save components
  - gameplay card data
- Grading submission validation, credit deduction, job creation, card consumption, and return spawning remain server-authoritative.
- Added targeted host/client presentation messaging without duplicating gameplay actions.
- Added multiplayer verification tooling and repeated release-build validation.
- Improved lifecycle cleanup for scene transitions, disconnects, destroyed presentation objects, and interrupted animations.
- Reduced excessive development logging while retaining useful startup summaries, configuration summaries, warnings, missing-asset errors, network errors, and save failures.

### Playtest Status and Remaining Work

- All 33 Set 1 cards are now implemented and eligible for booster rolls when their assets load.
- Booster Packs and both Booster Box types now use normal gameplay scrap spawning.
- Full booster opening presentation is implemented.
- Physical card variant presentation is implemented.
- Graded-card slab presentation is implemented.
- Animated grading submission pedestal is implemented.
- Grading return pedestal and delayed returned-card restoration are implemented.
- Additional host/client multiplayer testing is still recommended for:
  - booster reveal presentation
  - grading pedestal synchronization
  - slow Company-scene loads
  - returned-card placement
  - long-session save/persistence behavior
- V2 roadmap includes a ship collection display case and additional collection presentation features.
- V3 roadmap includes the upgrade shop and improved free-rotation inspection for cards, packs, and boxes.
- This remains a playtesting build and balance values may continue to change.

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