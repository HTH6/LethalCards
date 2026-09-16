# Lethal Cards

**Version 0.1.3 - playtesting release**

Collect Lethal Company trading cards, discover variants, open booster packs and boxes, and submit cards for grading at the Company. This build is for playtesting; balance, presentation, and multiplayer behavior are still being refined.

## Current Features

- Full 33-card Set 1 roster across five rarities.
- Light and Heavy Booster Packs containing three cards each.
- Standard and Golden Booster Boxes containing four sealed packs each.
- Standard, Foil, Alternate Art, and Misprint variants with independent discovery tracking.
- Vanilla card inspection and per-instance variant-aware scan names.
- Persistent collection and grading progress isolated by game save.
- Server-authoritative opening, grading, and returned-card pickup.
- Collection navigation and grading-status commands for hosts and remote clients.

## Set 1: 33 Cards

| Rarity | Count | Cards |
|---|---:|---|
| Common | 10 | Hoarding Bug, Eyeless Dog, Bunker Spider, Hygrodere, Baboon Hawk, Tulip Snake, Manticoil, Roaming Locusts, Backwater Gunkfish, Cadaver Growths |
| Uncommon | 9 | Snare Flea, Spore Lizard, Butler, Forest Keeper, Franklin, Circuit Bees, Cadaver Bloom, Mask Hornets, Giant Sapsucker |
| Rare | 7 | Thumper, Coil-Head, Bracken, Barber, Earth Leviathan, Kidnapper Fox, Feiopar |
| Ultra Rare | 4 | Jester, Nutcracker, Masked, Maneater |
| Secret Rare | 3 | Ghost Girl, Jeb, Lasso Man |

Only cards whose assets successfully load are eligible for pulls. Selection is uniform within each rolled rarity; duplicates are allowed. Individual cards do not naturally spawn as loose facility scrap.

## Booster Packs and Boxes

Use the normal primary item-use input while holding a pack or box. Control tips show **Rip Pack** or **Open Box** with the available input binding.

Normal packs use these slot-specific odds:

| Pack / slot | Common | Uncommon | Rare | Ultra Rare | Secret Rare |
|---|---:|---:|---:|---:|---:|
| Light 1 | 72.5% | 20% | 7.5% | 0% | 0% |
| Light 2 | 69% | 20% | 7.5% | 2.5% | 1% |
| Light 3 | 58% | 20% | 15% | 5% | 2% |
| Normal Heavy 1 | 30% | 55% | 10% | 5% | 0% |
| Normal Heavy 2 | 25% | 50% | 15% | 10% | 0% |
| Normal Heavy 3 | 20% | 40% | 20% | 15% | 5% |

Heavy packs have a **5% God Pack chance**, replacing the normal table. Each of the three God Pack slots independently rolls **60% Ultra Rare / 40% Secret Rare**. Normal Heavy packs do not guarantee a Rare-or-better card.

- **Standard Booster Box:** four independent rolls, each 90% Light / 10% Heavy. Chance of at least one Heavy: 34.39%.
- **Golden Booster Box:** exactly four Heavy packs.
- Boxes spawn sealed packs. Cards and variants are generated only when those packs are opened individually.

## Variants and Values

| Variant | Chance per card | Ungraded value multiplier |
|---|---:|---:|
| Standard | 69% | 1x |
| Foil | 25% | 1.5x |
| Alternate Art | 5% | 2x |
| Misprint | 1% | 3x |

Variant odds are identical for Light, Heavy, and God Pack cards. Registry base values are modified by variant and grading calculations using the existing rounding rules. Scan names include the variant, including `(Standard)`. More elaborate variant artwork is planned.

## Spawn Configuration

Settings are in `BepInEx/config/LethalCards.cfg`:

```ini
[Spawn Weights]
LightBoosterWeight = 30
HeavyBoosterWeight = 15
BoosterBoxWeight = 10
GoldenBoosterBoxWeight = 5
```

These are current code defaults: relative facility scrap selection weights, not percentages or physical carry weights. Existing config values override defaults. Set a weight to 0 to disable natural spawning for that item, and restart after editing. The host's configuration controls the round's scrap selection.

Settings from a single legacy `*.LethalCards.cfg` file are copied only if the new file does not yet exist. Once `LethalCards.cfg` exists, edit that file; the legacy file is retained but no longer used for these settings.

## Collection and Terminal Commands

Pulling a card permanently records that card and its specific variant for the current save, even if the physical card is later sold, graded, dropped, or lost. Viewing collection entries never unlocks them.

| Command | Purpose |
|---|---|
| `cards help` | List Lethal Cards commands. |
| `collection` | Open the collection: three cards per page, variant discovery, and totals. |
| `collection next` / `collection previous` | Move between collection pages. |
| `collection page <number>` | Open a numbered page. |
| `collection <card name>` | Show one card's collection details. |
| `next` / `prev` / `previous` | Navigate only while browsing collection. |
| `grading` / `grades` | Show active grading jobs and ready results. |

Pages are bounded and do not wrap. Unrelated commands, grading commands, terminal exit, and session reset clear collection context. Names are case-insensitive; `coilhead`, `coil-head`, and `coil head` resolve to Coil-Head.

## Grading

Submit an ungraded card at the Company's submission pedestal for **$10**. Grading takes **three grading-day advances**. Each job has its own submission day and stored result.

| Days since submission | Status | Results shown |
|---|---|---|
| 0 | Processing Order... | Variant-aware name only |
| 1 | Assessing Grade... | Variant-aware name only |
| 2 | Shipping Order... | Variant-aware name only |
| 3+ | Ready for pickup! | Variant-aware name, actual grade, and final scrap value |

Grading can reduce or increase value; it is not a guaranteed profit. The final value shown in the terminal is stored with the job and applied to the returned card.

Ready jobs remain visible until the physical returned card is picked up. Confirmed pickup removes the job; subsequent host and remote commands reflect that removal. Remote status requests are read-only and answered only to the requester. They contain no hidden grade or final value before day 3.

Existing grading saves remain supported. Older jobs without a stored final value derive it from their saved variant and grade without rerolling.

## Playtest Notes and Known Issues

- **Temporary debug behavior is active:** one Standard and one Golden Booster Box spawn on the ship when the host/session is ready. Natural facility spawning is separately enabled.
- Submission and return pedestals need new designs/models.
- Returned cards can float above the grading pedestal; placement needs correction.
- Vanilla card inspection is available; improved rotation controls are planned.
- Detailed diagnostic logging remains enabled for booster and box troubleshooting.
- Continue testing host/client opening, item handoffs, grading status, returned-card pickup, final values, save/reload, and save-slot isolation.
- The full roster and box opening have been confirmed in reported playtests. Not every feature or multiplayer scenario has completed gameplay validation.

## Planned Updates

### Needed for V1

- Revamp submission and graded-card pickup pedestals with new designs/models.
- Fix returned-card placement so cards no longer float above the grading pedestal.

### V2 Additions

- Ship collection display case for storing cards.
- Booster Pack opening animation sequence.
- More elaborate artwork for Alternate Art, Foil, and Misprint variants.

### V3 Additions

- Upgrade shop offering instant grading, better card values, better graded values, increased spawn weights, and increased card rarity rates in packs.
- Improved inspection with rotation controls for cards, booster packs, and booster boxes.

Roadmap entries are planned features, not included functionality or release-date commitments.

## Installation and Feedback

Install through a Thunderstore-compatible mod manager with BepInExPack and LethalLib. For manual installation, install those dependencies and place the Lethal Cards DLL and `lethalcards` AssetBundle together in the mod's BepInEx plugins folder.

For multiplayer, all players should use the same mod version and AssetBundle with the required dependencies.

For bug reports, include the mod version, host/client role, player count, affected item, reproduction steps, expected/actual behavior, and relevant BepInEx log output.
