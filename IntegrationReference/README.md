# Lethal Cards Reveal Integration Reference

These files are reference implementations from the completed Unity
prototype.

## CardRevealPrototype.cs.reference

Working prototype for:

- sequential three-card booster reveal
- rarity-specific reveal timing
- Ultra and Secret buildup
- pack rumble
- buildup sparkles
- reveal bursts
- Secret rainbow ring
- reveal flashes
- lingering glows
- scale punch
- reveal audio
- anticipation audio

This is NOT production code.

Do not copy its test-only systems into production, including:

- TestRevealRarity
- keyboard test input
- hard-coded test card references
- test rarity fields

Adapt the working presentation behavior to the existing production
booster/card-generation architecture.

## CardVariantVisuals.cs.reference

Working prototype for persistent physical-card variant visuals:

- Standard
- Foil
- Alternate Art
- Misprint

Variant visuals must eventually use the existing production card
variant rather than introducing a second variant roll/system.

Important texture properties:

Normal HDRP/Lit card material:
_BaseColorMap

Alt Art Shader Graph:
_BaseMap

Foil, Alt Art, and Misprint visuals must persist on the actual
physical card object after the booster reveal has ended.