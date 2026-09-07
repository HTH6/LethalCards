You are helping me develop a Lethal Company mod called Lethal Cards.

Before making any changes, inspect the project structure and relevant files so you understand how the systems work together. Do not broadly refactor working systems unless necessary. Prefer small, targeted changes and explain what you intend to change before editing.

Current project state:

CORE CARD SYSTEM
- The mod adds collectible trading cards as Lethal Company scrap.
- Current cards include:
  - Hoarding Bug
  - Eyeless Dog
  - Snare Flea
  - Bracken
  - Coil-Head
  - Jester
  - Ghost Girl
- Cards have unique card IDs, rarity, base scrap value, variants, and grading data.
- Current variants include Standard and support is being built for Foil, Alternate-Art, and Misprint.
- CardInstanceData stores/restores the card definition, variant, grade, and calculated scrap value.
- Cards are NetworkObjects and must behave correctly in multiplayer.

BOOSTER SYSTEM
- Light and Heavy booster packs exist.
- Booster packs spawn cards when opened.
- Heavy packs have a 2% God Pack chance.
- Booster opening is intended to be server-authoritative.
- Collection progress is unlocked when a card is pulled, regardless of whether the physical card is later sold or graded.
- Planned later additions include Booster Bundles containing 2 packs and Booster Boxes containing 5 packs.

COLLECTION SYSTEM
- Collection progress persists per Lethal Company save.
- The collection records cards and variants that have been discovered.
- SaveIdentityManager creates a unique identity for each Lethal Company save so grading/collection files do not collide between saves.
- Save identity is cached in memory after loading to prevent repeated ES3 reads/log spam.

GRADING SYSTEM
- Players can submit an ungraded card at a grading pedestal at the Company.
- Grading currently costs $10.
- A GradingJob persists:
  - JobId
  - CardId
  - Variant
  - Grade
  - SubmittedDay
  - ReadyDay
- Returned cards remain represented by their GradingJob until the physical returned card is actually picked up.
- GradingReturnData links the spawned returned card to its JobId.
- Picking up the returned graded card claims/removes that job.

CURRENT TEST MODE
- InstantGradingForTesting is enabled during development, so submitted cards become immediately ready.
- In normal gameplay grading is intended to take multiple days.

GRADED CARD RETURN SYSTEM
- There is a submission pedestal and a separate graded-card pickup pedestal at Gordion/the Company.
- GradingReturnSpawner finds ready jobs and spawns their physical graded cards.
- A HashSet of spawned JobIds prevents the same ready grading job from spawning multiple physical copies during the same Company visit.
- Do NOT remove a GradingJob merely when its card spawns. It should remain until the player picks up the returned card.
- The spawned JobId set is reset when the Company scene/session state is reset.
- When instant grading creates another job, we call TrySpawnReadyCards() without resetting the spawned JobId set.

PEDESTAL PLACEMENT
- Returned graded cards were previously falling through/inside the pedestal.
- This is now fixed using GradingReturnPedestalLock.
- GradingReturnPedestalLock holds each returned card at an explicit display position in LateUpdate until the player grabs it, then destroys itself and allows normal GrabbableObject behavior again.
- The cards stand vertically on the pedestal.
- Their root/display Y is approximately -0.88 based on their collider/renderer bounds and the pedestal surface height.
- Multiple returned cards should be slightly spread across the pedestal rather than stacked.
- GradingReturnSpawner uses a spawnIndex to calculate card offsets.
- The spawn index must account for cards already spawned this visit; otherwise each new grading submission starts again in slot 0.

RECENT FIXES
1. Fixed SaveIdentityManager repeatedly logging "SAVE IDENTITY LOADED" by caching the loaded identity.
2. Fixed duplicate graded card returns by tracking spawned JobIds.
3. Fixed graded cards falling into the pedestal using GradingReturnPedestalLock.
4. Filtered ready grading jobs against spawned JobIds so "GRADING RETURNS READY" does not spam continuously.
5. Spread returned cards using spawn-index-based pedestal positions.

IMPORTANT DEVELOPMENT RULES
- Preserve multiplayer/server authority.
- Avoid client-created authoritative game state.
- Preserve grading and collection persistence.
- Do not remove grading jobs before physical pickup.
- Do not change working booster/card systems unless required by the task.
- Keep changes narrowly scoped.
- If you find a potential architectural issue unrelated to the current task, tell me about it but do not automatically refactor it.

First:
1. Inspect the project and identify the main files/classes involved in cards, boosters, grading, collection saving, and networking.
2. Summarize your understanding of how they interact.
3. Point out any discrepancies between the code and the system description above.
4. Do not edit anything yet.