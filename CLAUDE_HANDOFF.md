# Codex → Claude handoff — Hive Avatar

## User direction at handoff

The user asked Codex to recover the entire Claude conversation and memory, then continue. Audio output now
works; transcription was already confirmed. They delegated demo priorities, then narrowed Codex's scope:
**finish 3D modeling / animation assets, write this handoff and the TODOs, then STOP. Claude owns scripting.**
Do not interpret the new suggestions below as completed features.

Branch: `armory-vr`. Everything from this Codex session is **uncommitted and unpushed**. Do not add co-author
trailers. The workspace had existing generated-file, log and XR-settings changes before this session.
Do not bulk-stage logs, generated project files, settings, `UserSettings.zip`, or the crash recovery scene.

## Art package delivered

Open `Assets/Game/Art/HiveAvatar/HiveAvatarVisual.prefab`.

- Uses the project's existing rigged `Assets/AlienAnimal/Alien Animal_Fbx_7.4.fbx` and its original textures.
  No external model was downloaded. Preserve the original model's provenance/license information.
- Adds eight faceted chitin scales, a reusable `ChitinScale.asset` mesh, `HiveChitin.mat`, and
  `HiveOrganEmission.mat`. Armor and organ visuals follow the skeleton.
- Four empty socket transforms: `LeftClaw_Socket`, `RightClaw_Socket`, `SporeSac_Socket`, `Crest_Socket`.
  Each has a small visual core. They have **no damage colliders or gameplay scripts**.
- Animator controller: `HiveAvatarVisual.controller`. Model root motion is off.
  Parameters: `Moving` (bool), `Claw`, `Bite`, `Die` (triggers).
  `Moving` selects idle/walk. Claw and bite return to idle after the clip. Death has no exit transition.
  Idle and walk have explicit self transitions to repeat imported clips without changing the FBX importer.
- Additional Jump and Rest states are available for direct `Animator.CrossFade` calls. They are not wired to
  input. These are existing imported clips, not newly authored motion-capture or a custom stomp animation.
- Existing clips mapped: Idle_Aggressive, Walk-Cycle, Attack_Hit, Attack_Bite, Die_1, Jump, Rest.

Preview: [boss art render](docs/superpowers/handoff/hive-art-preview.png).

**Integration distinction:** the standalone art prefab is not yet used by `EnemyFactory`. The earlier runtime
prototype still instantiates the original FBX through `HiveAvatarAssets` and adds its own larger targets and
rectangular plates. Claude should integrate the art prefab and avoid duplicating those visual parts.
The prototype's `HiveAvatar.Play` also clears the Animator controller and drives clips through Playables;
choose either that path or the new controller when integrating, not both.

## Gameplay prototype already written before the user narrowed scope

Preserved for Claude to review, reuse, or replace. It is not a polished final encounter.

- `Assets/Game/Core/HiveAvatarState.cs`: four 180-HP organs; each disables one attack. One active resistance
  replaces its predecessor, so weapon switching remains effective.
- `Assets/Game/Enemies/HiveAvatar.cs`: model placement, four-second entrance, manual animation playback,
  organ visuals, health display, plating, and an attack coroutine. Base health: 2400. Enrage below 35% shortens
  pauses. Arrival is invulnerable. Direct organ hits deal 1.75× body damage.
- `HiveThreat.cs`: shootable spore pods (32 HP, hatch after 5 seconds) and wreckage (65 HP, impacts at 6 seconds).
- `HiveTelegraph.cs`: floor circle with a countdown arc.
- `HiveAvatarAssets.cs` + `Resources/HiveAvatarAssets.asset`: build-safe model and five clip references.
- `Assets/Game/Editor/HiveAvatarSetup.cs`: prepares references and adds a jump-to-boss menu.
- `HiveAvatarVerification.cs`: live smoke check; menu `Armory → Verify Hive Avatar (Play Mode)`.
- `Enemy` gains an externally-driven path and optional hit position. Boss damage bypasses accumulated wave
  counters; its own replaceable plating applies instead. Boss death clears threats and leaves 3.5 seconds for
  the death clip. `EnemyFactory` creates the Avatar for the boss kind.
- Projectile, beam and direct splash hits forward impact positions. Piercing beams deduplicate multiple
  colliders on the same enemy.
- `Mothership.BossAdapt` updates the active Avatar every 20 seconds. Global adaptation for ordinary enemies is
  unchanged. `WaveDirector` places the boss on the player's side of the arena for a visible reveal.
- `ArmoryGame` hides the existing top-level alien scene prop **at runtime only** to avoid duplicate aliens.
  No map edits were intentionally saved to `SpaceStation.unity`.
- Bridge command `boss` and menu `Armory → Jump To Hive Avatar (Play Mode)` jump to wave 5.

Current attacks: left/right claw sweep (3-second warning circle), spore burst, and incoming wreckage.
Breaking the matching claw, sac or crest disables that attack. These attacks are prototypes.

## What damages what today

**There is no player health or personal death.** Core integrity is the only loss condition. At zero, the wave
restarts. Wreckage costs 18 core HP, swarmers attack the core, and failing to leave a sweep circle costs 12 core
HP. That last rule was a compatibility choice, not the preferred final design.

The user asked whether a better boss arena exists and whether they can die. We discussed an open cargo deck or
exterior docking platform with clear sightlines, low cover, perimeter teleport pads, and a small off-center core.
We also proposed player shields/HP for direct boss attacks, keeping wreckage/swarmers as core threats.
Both are **proposals**, recorded in the TODO, not implemented or fully approved designs.

## Known issues Claude should address first

1. **Left-claw targeting can be occluded by the body box collider.** A live ray from directly in front of the
   left claw hit `Boss` first, about 2.22 m from the organ center. Other three organs hit their own collider.
   The direct-call smoke test does not catch this. Fix collider geometry/target routing and add a ray-based test.
2. The circular station's central hologram and architecture obstruct the fight from some views. The art is
   readable from a clear angle, but this arena is not ideal for a large boss and ground hazards.
3. All four organs can disable all attacks. Decide whether the exposed final phase should remain a reward or
   gain a deliberately limited fallback attack. Weapon mimic, shield choir and acid pools were not implemented.
4. Sweep and wreck damage hit the core, not the player. Implement player HP if the team adopts that proposal.
5. Art prefab integration still needs scripting; runtime rectangular plates are not the authored chitin mesh.
6. Animation/controller death gating needs gameplay ownership: avoid firing other Any State triggers after Die.
7. Encounter scale/collider layout is provisional; recheck from the headset and the eventual boss arena.

## Validation and editor state

- Baseline: 29 EditMode tests passed before changes.
- Seven new encounter-rule cases failed against stubs, then all **36 EditMode tests passed** after implementation.
- Corrected a live scale bug: this FBX has 100× renderer scale. `BakeMesh(mesh, true)` plus TransformPoint gave
  the correct normalization; the default overload applied the scale twice in the sizing calculation.
- Live smoke check passed: visible imported model, replacement resistance, all four organ breaks, shootable
  pod damage, death removal/collider cleanup, no remaining threats, fresh restart. Evidence:
  [live checks](docs/superpowers/handoff/hive-runtime-verification.txt).
- Viewed both the live encounter capture and the standalone art render. This is desktop validation, not a
  headset playtest. The new art prefab has no missing scripts.
- Unity suffered a native crash with OpenXR in the stack before the new runtime compiled. It was reopened.
  An `Assets/_Recovery` scene appeared; it was left alone.
- One later test run overlapped entering Play Mode and produced Test Runner cleanup errors. That is a tooling
  sequencing failure, not a passing test result. Wait for tests to finish before entering Play Mode.
- For desktop checks XR startup was temporarily disabled in memory. It was restored to **true**, normal
  time scale restored to **1**, and Play Mode stopped before authoring the art package.
- Unity also regenerated project/log/settings files. Review those separately; don't fold them into boss work.

## New user TODOs

The canonical list is [docs/superpowers/TODO.md](docs/superpowers/TODO.md), including:

- Fabrication friction: finite per-wave energy, visible costs before confirmation, partial replacement refund,
  cheaper upgrades, a free basic fallback, and possible rewards for destroying boss organs. Numbers need tuning.
- Telegraph a stomp followed by an expanding ground shockwave. Do not require a jump input to escape.
- Add dodgeable fireballs, potentially shootable/deflectable.
- Add a charged sweeping laser, a safe sector and a recovery window.
- Consider the dedicated open boss arena and player shields/HP described above.

Earlier priorities remain: untimed ARMORY phase, conversation, latency, spectator view, melee, movement,
elements/budget, sketch board, tutorial/story, observability and polish. Do not silently treat them as done.

Codex stopped at this handoff as requested. Claude continues scripting.
