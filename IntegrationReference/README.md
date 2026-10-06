# Lethal Cards Reveal Integration Reference

These files are reference implementations from the completed Unity
prototypes.

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

## GradingScannerAnimationTest.cs.reference

Working Unity prototype for the animated grading submission pedestal.

The production pedestal prefab is:

GradingSubmissionPedestal_Animated

Working prototype behavior includes:

- idle grading screen state
- blinking text cursor
- CRT screen flicker
- occasional screen/text glitch effect
- green and red status indicator states
- submitted-card presentation
- scanning delay
- moving scanner beam
- scanner fan/light tracking the moving beam
- scanner spotlight
- scanner audio
- animated SCANNING / SCANNING. / SCANNING.. / SCANNING... text
- animated card-slot opening and closing
- submitted-card drop animation
- card-drop audio
- successful submission state
- temporary insufficient-credits error state
- red screen emission for insufficient credits
- NOT ENOUGH CREDITS message
- automatic return to idle state

This is NOT production code.

Do not copy its test-only systems into production, including:

- keyboard Space-bar test input
- ScannerTestCard / hard-coded test card reference
- direct ownership of grading validation
- direct ownership of grading economy logic
- assumptions that the animated card is the authoritative networked card

The production implementation must adapt this presentation behavior to
the existing grading architecture.

The existing grading system must remain responsible for:

- validating whether a card can be submitted
- determining grading cost
- checking available credits
- deducting credits
- creating grading jobs
- consuming/removing the submitted card
- multiplayer/server authority
- grading turnaround
- save data

The animation controller should only provide presentation and feedback.

For successful grading submissions, the production implementation should
trigger the scanner sequence only after the existing grading logic has
accepted the submission.

For insufficient credits, the existing insufficient-credit branch should
trigger the pedestal's error presentation without:

- deducting credits
- consuming the card
- creating a grading job
- starting the normal scanner sequence

The insufficient-credit presentation should temporarily:

- disable the green status light
- enable the red status light
- change the screen emission to red
- display:

  NOT ENOUGH
  CREDITS

- restore the original screen emission and idle state afterward

The prototype modifies the DisplayScreen material's `_EmissiveColor`.
The production implementation must preserve the authored idle emission
color and allow the CRT flicker system to operate against the currently
active base emission color.

Important prefab child names used by the production integration:

- CardSubmissionAnchor
- CardSlotOpening
- DisplayScreen
- ScreenText
- ScreenScanlines
- StatusGreen
- StatusRed
- InteractionAnchor
- ScanStartPoint
- ScanEndPoint
- CardDropPoint
- ScanFanPivot
- ScanBeamFan
- ScanSpotLight
- ScanBeam
- ScanAudioSource
- CardDropAudioSource

These references should be resolved and cached when the production
pedestal initializes rather than repeatedly searched for during the
animation.

The production AssetBundle prefab must NOT depend on
GradingScannerAnimationTest itself. The `.reference` file exists only so
Codex can reproduce the working Unity prototype behavior inside the
Lethal Cards DLL.

The production integration should avoid moving the authoritative
networked card purely for cosmetic animation. Prefer a safe local
visual-only presentation copy when necessary while allowing the
existing grading system to continue controlling the real card.

Do not change unrelated working systems while integrating the animated
grading pedestal, including:

- grading probabilities
- grading value multipliers
- grading turnaround
- grading save data
- grading terminal commands
- grading return pedestal/grid
- graded-card slab presentation
- booster probabilities
- card variants
- scrap spawning
- held-item offsets
- floor-placement behavior