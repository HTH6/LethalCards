# Lethal Cards

**Lethal Cards is currently in beta testing.**

Lethal Cards adds collectible trading cards to Lethal Company. Find booster packs as scrap, rip them open for cards, collect different variants, submit cards for grading, and sell valuable pulls for scrap.

The mod is still actively being developed. Multiplayer behavior, balancing, grading, persistence, card values, and other systems may change during the beta.

## Features

* Collectible Lethal Company-themed trading cards
* Light and Heavy Booster Packs
* Three cards per booster pack
* Card rarity system
* Standard, Foil, Alternate-Art, and Misprint variants
* Variant-based card value multipliers
* Persistent collection tracking
* Unique card IDs
* Card grading at the Company
* Graded card value multipliers
* Persistent grading jobs
* Returned graded-card pickup pedestal
* Per-save collection and grading persistence
* Multiplayer networking support currently under active testing

## Current Cards

The current beta set includes:

* Hoarding Bug
* Eyeless Dog
* Snare Flea
* Bracken
* Coil-Head
* Jester
* Ghost Girl

More cards and future card sets are planned.

## Booster Packs

Booster Packs can appear as scrap and can be opened to receive three cards.

Booster spawn weights are configured in `BepInEx/config/HunterHaaf.LethalCards.cfg`,
generated when the plugin first runs:

```ini
[Spawn Weights]
LightBoosterWeight = 12
HeavyBoosterWeight = 4
```

These are relative selection weights, not percentages or carry weights. Set a
weight to `0` to disable natural spawning for that pack type. Restart the game
after editing the config. The host's settings determine the round's scrap rolls.
Individual cards do not spawn as map scrap; they come from booster packs.

### Light Booster Pack

Contains three cards with the standard pack rarity distribution.

### Heavy Booster Pack

Contains three cards with improved opportunities for higher-rarity pulls.

Heavy Booster Packs also currently have a **2% chance to become a God Pack**.

## Card Variants

Cards can currently roll as:

* **Standard — 90%**
* **Foil — 7%**
* **Alternate-Art — 2%**
* **Misprint — 1%**

Variants affect the value of the card.

Separate visual artwork/effects for every variant are still under development.

## Collection

Opening a card from a booster permanently registers that card and its variant in your collection.

You do not need to keep the physical card afterward. Once discovered, it remains registered even if you sell it or submit it for grading.

Collection progress is stored separately for each Lethal Company save.

Type `collection` in the terminal to see all cards, or `collection <card name>`
to see one card's discovery status and discovered variants. Names are
case-insensitive; `collection coilhead`, `collection coil-head`, and
`collection coil head` all find Coil-Head. Viewing a card does not unlock it.

## Card Grading

Cards can be submitted at the grading pedestal located at the Company.

A graded card receives a grade and increased scrap value. Once grading is complete, returned cards appear on a separate pickup pedestal.

During the current beta, grading behavior may be accelerated or otherwise configured for testing purposes.

## Beta Notice

This release is intended primarily for testing.

Areas currently being tested include:

* Multiplayer synchronization
* Remote-client grading
* Returned graded-card pickup
* Booster opening synchronization
* Card value synchronization
* Save persistence
* Save-slot isolation
* Balance and scrap values
* Booster rarity distributions

Please expect bugs and balance changes.

For multiplayer testing, **all players should install Lethal Cards and its required dependencies**.

## Installation

The recommended installation method is through a Thunderstore-compatible mod manager such as r2modman or Thunderstore Mod Manager.

Required dependencies are installed automatically when using a compatible mod manager.

### Manual Installation

Install the required dependencies, then place the Lethal Cards plugin files inside your Lethal Company BepInEx plugins directory.

## Current Beta Version

**0.1.0**

Initial public beta release.

## Feedback and Bug Reports

If you encounter a bug, please include as much information as possible, especially:

* Whether you were the host or a client
* Number of players
* What card or booster was involved
* What you expected to happen
* What actually happened
* Relevant BepInEx log output

Multiplayer reports are especially helpful during the beta.
