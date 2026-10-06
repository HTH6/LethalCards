# Lethal Cards

[![Watch the Lethal Cards Trailer](https://img.youtube.com/vi/Ap0-935wEuE/maxresdefault.jpg)](https://www.youtube.com/watch?v=Ap0-935wEuE)

### ▶ Watch the Official Lethal Cards Trailer by clicking the link above

**Hunt for Packs. Rip Them. Collect Cards. Grade Them.**


Want to support my work? Buy me a coffee via Venmo :)  
**@hdaddyo**

Have suggestions? Add me on discord: @Hdaddyo
---

## What is Lethal Cards?

**Lethal Cards** adds collectible trading cards to Lethal Company. Search facilities for booster packs and booster boxes, rip them open, hunt for rare cards and variants, build a persistent collection, and send your best pulls off for grading at the Company Building.

Lethal Cards is designed for multiplayer, and **all players in the lobby should have Lethal Cards installed** for everything to function correctly.

- [Thunderstore](https://thunderstore.io/c/lethal-company/p/WaterCupKing/LethalCards/)
- [GitHub](https://github.com/HTH6/LethalCards/)

---

## How It Works

1. **Find booster packs and booster boxes** inside facilities.
2. **Rip booster boxes or booster packs** to reveal four booster pack objects or three collectible cards respectively.
3. **Collect cards** across multiple rarity tiers and variants.
4. **Grade valuable pulls** at the Company Building. By default, grading takes **3 in-game days**.
5. **Build your collection** and use the terminal's `collection` command to see what you've obtained.
6. Check submitted cards using the `grading` command and pick up completed graded cards once they're ready.

---

## Features

### 33 Collectible Cards

Lethal Cards v1.0.0 includes a full **33-card Set 1**, featuring creatures and characters from across Lethal Company.

Cards are divided into five rarity tiers:

- Common
- Uncommon
- Rare
- Ultra Rare
- Secret Rare

### Card Variants

Every card can appear as one of four variants:

- **Standard**
- **Foil**
- **Alternate Art**
- **Misprint**

Variants increase the value of a card, with rarer variants providing larger value multipliers.

### Booster Packs

Every booster pack contains **3 cards**.

#### Light Booster Pack

The standard booster pack, with mostly Common and Uncommon pulls while still offering chances at higher rarities.

#### Heavy Booster Pack

A more valuable booster pack with significantly improved Rare and Ultra Rare odds, plus a chance at Secret Rare cards.

Heavy Booster Packs also have a chance to become special high-rarity **God Packs**.

### Booster Boxes

#### Standard Booster Box

Contains **4 booster packs**.

Each pack has a chance to be either:

- Light Booster Pack
- Heavy Booster Pack

#### Golden Booster Box

Contains **4 guaranteed Heavy Booster Packs**.

### Grading System

Take valuable cards to the **Company Building** and submit them to the grading machine.

Cards receive a grade from **1 through 10**.

Higher grades can significantly increase a card's final value.

By default:

- Grading costs **$10 per card**
- Grading takes **3 in-game days**
- Completed cards can be picked up from the grading return station

Graded cards are returned inside a collectible slab displaying information such as:

- Card name
- Variant
- Set number
- Grade
- Grade descriptor
- Final graded value

### Persistent Collection

Your card collection persists between sessions.

Use the terminal to:

- Browse collected cards
- Search for specific cards
- View collected variants
- Navigate collection pages
- Check grading status

### Multiplayer Support

Lethal Cards supports multiplayer and synchronizes booster packs, booster boxes, pack opening, card rewards, collection updates, grading, and related gameplay across players.

**All players should have Lethal Cards installed.**

---

## Terminal Commands

### Collection

`collection`

Displays your card collection.

`collection next`

Go to the next collection page.

`collection previous`

Go to the previous collection page.

`collection page X`

Jump directly to a collection page.

Example:

`collection page 3`

You can also search for a specific card:

`collection bracken`

While browsing the collection, you can also use:

- `next`
- `prev`
- `previous`

### Grading

`grading`

Displays the current grading queue and status of submitted cards.

`grades`

Alias for `grading`.

Cards progress through statuses such as:

- Processing Order...
- Assessing Grade...
- Shipping Order...
- Ready for pickup!

Once ready, the card's grade and final value are shown.

---

## Configuration

Lethal Cards includes configurable balance and spawn settings.

Configuration files are generated through BepInEx after launching the game with the mod installed.

### Scrap Spawn Weights

Default v1.0.0 spawn weights:

- `LightBoosterWeight = 45`
- `HeavyBoosterWeight = 25`
- `BoosterBoxWeight = 20`
- `GoldenBoosterBoxWeight = 10`

These are **relative spawn weights**, not direct percentages.

Setting a spawn weight to `0` disables that item from naturally spawning as facility scrap.

Existing configuration files may preserve values from older versions, so check your config after updating.

A game restart is recommended after changing spawn settings.

For multiplayer, the **host's gameplay configuration should be treated as authoritative**.

### Booster Values

Default booster and box values:

- Light Booster Pack: **$30**
- Heavy Booster Pack: **$50**
- Standard Booster Box: **$75**
- Golden Booster Box: **$150**

### Grading Configuration

The grading system can be configured, including settings such as:

- Grading cost
- Grading turnaround time
- Grade probabilities
- Grade value multipliers

Default grading turnaround:

**3 in-game days**

Default grading cost:

**$10**

### Variant Odds

Default card variant rates:

- Standard: **69%**
- Foil: **25%**
- Alternate Art: **5%**
- Misprint: **1%**

### God Packs

Default Heavy Booster God Pack chance:

**5%**

---

## Installation

### Recommended Installation

Install Lethal Cards through:

- **r2modman**
- **Thunderstore Mod Manager**

Once Lethal Cards is available on Thunderstore:

1. Open r2modman or Thunderstore Mod Manager.
2. Select Lethal Company.
3. Create or select your profile.
4. Search for **Lethal Cards**.
5. Install the mod and its dependencies.
6. Launch the game through the mod manager.

### Multiplayer Installation

Every player joining the lobby should have:

- Lethal Cards
- Required dependencies
- Compatible mod versions

installed.

---

## Dependencies

Lethal Cards currently requires:

- **BepInExPack 5.4.2305**
- **LethalLib 1.2.0**

Dependency information will also be listed on the Thunderstore package page.

---

## Planned Future Features

Lethal Cards v1.0.0 is only the beginning.

### V2

#### Collection Display Case

Add a display case to the ship where players can store and show off their favorite cards.

#### Break & Re-Grade Cards

Allow graded cards to be removed from their slabs and submitted for grading again with different re-grading odds.

#### New Booster Box, Booster Pack, and Icon Art

Remove AI-generated artwork on booster boxes, booster packs, and mod icon and replace with hand-drawn images.

### V3

#### Upgrade Shop

Planned upgrades include:

- Instant grading
- Increased card values
- Improved graded card values
- Increased booster spawn weight
- Improved card rarity rates inside booster packs

#### Improved Item Inspection

Expanded inspection controls allowing players to rotate and examine:

- Cards
- Booster Packs
- Booster Boxes
- Graded Cards

### V4

#### Play the Lethal Card Game

Use the cards you've collected to actually **play a Lethal Cards card game** with other players.

More information will come as development continues.

---

## AI-Usage Notice

AI was utilized to help build the code logic behind the mod. All card artwork itself was done by hand. AI was used to generate the booster box and booster pack art for those item assets themselves, but not the 33 individual cards.

---

## Stability

As it is, this mod runs perfectly fine with any other Lethal Company Mod. This mod does not interact with or require any other mods besides the basic dependencies listed above, and thus has no issues rendering content or interacting with mod content. I currently am running this mod in conjunction with 99+ other mods on my own personal Lethal Company mod profile without any stability issues. If you run into any issues, please list them utilizing the README sections below.

---

## Known Issues

Known issues will be tracked through GitHub.

If you encounter a problem, please include:

- What happened
- Steps to reproduce the issue
- Whether you were the host or a client
- Your Lethal Cards version
- Relevant `BepInEx/LogOutput.log` files

---

## Bug Reports

Found a bug?

Please report it through GitHub Issues:

[GitHub Issues](https://github.com/HTH6/LethalCards/issues)

Logs are extremely helpful for multiplayer and networking problems.

---

## Feedback & Suggestions

Have an idea for a card, feature, balance change, or future update?

Feel free to leave feedback through:

- [GitHub](https://github.com/HTH6/LethalCards/issues)
- [Thunderstore](https://thunderstore.io/c/lethal-company/p/WaterCupKing/LethalCards/)
- Add me on discord and send me a message for suggestions - @hdaddyo

---

## Credits

### Created By

**hdaddyo**

### Development

WaterCupKing or Hdaddyo, both are me! Github is HTH6

### Artwork / Assets

-Eminizerbunny made all the art! Shoutout to her!

### Testing

Thanks to the following playtesters:
-Peakdog
-Bradford
-Eminizerbunny
-Mavericks
And to all my friends for their support throughout the entire process.

### Special Thanks

Special thanks to eminizerbunny for all the card artwork, couldn't have done it without her!
Special thanks to Peakdog for playtesting and mod support + ideas
Special thanks to Bradford for helping so, so much throughout the process

---

## Version

**Lethal Cards v1.0.0**

Set 1 includes:

- 33 Cards
- 5 Rarity Tiers
- 4 Variants
- 2 Booster Pack Types
- 2 Booster Box Types
- Full Grading System
- Persistent Collection Tracking
- Multiplayer Support

Thanks for playing **Lethal Cards**.

## License

Lethal Cards is licensed under the [MIT License](LICENSE).