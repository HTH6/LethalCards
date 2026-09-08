# Multiplayer test build

Build with `dotnet build -c Release` from this directory. `global.json` selects
.NET SDK 8.0.4xx; the pinned Netcode patcher runs automatically. The project targets
the installed game's Unity 2022.3.62 / Netcode 1.12 API. Standard .NET Framework
MSBuild is rejected because it would skip patching.

The patcher integration follows its [upstream instructions](https://github.com/EvaisaDev/UnityNetcodePatcher#msbuild).
Its generated serialization startup methods are invoked by `Plugin.Awake`.

Install **only `bin/Release/netstandard2.1/LethalCards.dll`**, alongside the existing
`lethalcards` asset bundle, in both players' mod profiles. Use matching game and
LethalLib versions. The patcher also creates `LethalCards_original.dll` as a build
backup; do not install that file or another older copy of the plugin.

Optional build/API verification in PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/VerifyMultiplayerBuild.ps1
```

The script uses the Mono.Cecil dependency in the local NuGet cache; override
`-CecilPath`, `-GameManagedPath`, or `-AssemblyPath` if their locations differ.
It checks generated RPC/variable registration and the installed game's pickup and
lifecycle hooks. It does not simulate Unity or a network session.

Current development settings remain: immediate returns, forced grade 10, and
actual pedestal payment of $10. `FreeGradingForTesting` is still unused by the
pedestal. One Light and one Heavy test pack spawn for the host each session.

## Host/client checklist

Use a test save and two separate game processes. Check both players' BepInEx logs.

1. **Join and collection:** join a save with existing discoveries. Both terminals
   should show the same collection before opening anything. Disconnect/rejoin
   and confirm it is still correct.
2. **Booster opening:** each player opens Light and Heavy packs. Expect three
   matching cards per pack, one consumed pack, and no stuck slot/icon/weight.
   Both players should be able to equip and use another item afterward. Repeated
   activation must not produce extra pulls. RNG and collection writes occur only
   on the host.
3. **Card state:** compare scan values, variants in host logs, and grade labels.
   Also check a naturally spawned card. Save cards aboard ship, quit/reload, then
   compare values and metadata again.
4. **Company presentation:** both players see both pedestals and the submission
   prompt. Return cards stand at the same positions for each player.
5. **Submission:** submit from each player, including several consecutive client
   submissions. Each consumes the correct card and exactly $10 once. The client
   must have a usable empty inventory slot afterward. Returns use distinct slots.
   Reject non-cards, already graded cards, and submission with fewer than $10.
6. **Pickup:** client picks up a return while host watches. Expect one
   `GRADING JOB CLAIMED` log and removal of that job from the host's grading file.
   Dropping/regrabbing must not claim again or relock the card. Try simultaneous
   pickup by both players; only the accepted pickup should claim the job.
7. **Unclaimed returns:** leave at least one return on the pedestal, take off,
   then land at the Company again without changing destination. Expect exactly
   one physical return for each unclaimed job. Repeat via another moon. Picked-up
   cards remain ordinary player-owned scrap and must not respawn as returns.
8. **Session persistence:** quit with an unclaimed job, reload, and collect it.
   Switch to another save slot and back without restarting the game. Collections,
   grading days/jobs, pedestals, and return tracking must not leak between saves.
9. **Regression checks:** sell/drop/store ordinary cards and boosters; verify
   vanilla inventory handling still works. If another mod enables late joining
   while landed, also check existing returns and graded cards after a late join.

For failures, record the step, which player acted, and both logs. Networking
exceptions, missing RPC handlers, serialization errors, duplicate job claims,
or disagreement in values are test failures even if the interaction looks correct.
