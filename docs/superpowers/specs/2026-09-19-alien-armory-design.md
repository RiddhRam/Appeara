# Alien Armory — VR scene + AI design

Date: 2026-09-19 · Scope: VR scene, weapon interpreter (OpenAI), audio/voice (ElevenLabs), waves + mothership adaptation.
Out of scope: multiplayer/netcode (pivot is single-player), generated 3D meshes.

## Loop

Aliens attack → player holds **left grip** and describes a weapon → speech-to-text → OpenAI returns a
`WeaponSpec` (strict JSON schema) → Unity assembles the weapon in the right hand from modular parts and
existing behaviour components → player fires with **right trigger** → wave ends → mothership reads usage
stats and picks counters → player invents something new.

Target demo (5–7 min): W1 machine gun · W2 swarm → bouncing plasma grenade · W3 armored → piercing railgun ·
Boss gains resistance to the most-used payload/modifier live → one last ridiculous weapon.

## Player / VR

- Rig: port the proven `QuestLinkRig` pattern (plain OpenXR + Input System, works over Quest Link).
- Locomotion: **teleport between fixed pads** (3–5 pads on the station). Left thumbstick forward shows an
  arc/ray; release snaps to the pad nearest the aim point. Right thumbstick = 45° snap turn.
- Left grip hold = push-to-talk. Left X = recenter. Right trigger = fire. Right B = drop to default gun.
- Desktop fallback (no headset): mouse aim, LMB fire, `T` opens a text prompt box, `1–5` load canned
  prompts. Needed for fast iteration and as a stage backup.

## WeaponSpec (the only thing the LLM controls)

```
name: string                  shipAILine: string (≤ 20 words, spoken by ship AI)
fireMode: projectile | beam | thrown
payload: kinetic | explosive | plasma | electric | cryo
modifiers: [ homing | piercing | bouncing | sticky | proximity ]   (0–3)
onHit:     [ splash | chain | slow ]                               (0–2)
fireRate (shots/s), projectileCount, spreadDeg, projectileSpeed, damage
visual: body (0–5), barrel (0–5), primaryColor (hex), projectileShape (orb|bolt|disc|mine), trail (bool)
sfxPrompt: string (short description of the firing sound)
```

- Unity **clamps** every number and applies a **power budget** (more modifiers → lower damage/fire rate),
  so the LLM cannot make a god-weapon. Unknown enum values fall back to defaults; parse failure → last
  weapon kept + ship AI "couldn't parse that" line.
- Each enum maps to a C# behaviour component (`HomingMover`, `Piercer`, `Bouncer`, `StickyProximity`,
  `SplashOnHit`, `ChainOnHit`, `SlowOnHit`, `BeamEmitter`). Never generated code.
- Models: predefined parts (primitives now, sci-fi pack pieces later) tinted by `primaryColor`.
  Stretch: gpt-image "blueprint" hologram generated async beside the weapon.

## Enemies

| Type      | Behaviour                   | Weak to                  | Resists              |
|-----------|-----------------------------|--------------------------|----------------------|
| Swarm     | many, small, low HP         | splash, chain            | single-target kinetic|
| Armored   | slow, high HP               | piercing, explosive      | kinetic, plasma      |
| Fast      | zig-zag sprint              | homing, slow, cryo       | slow projectiles     |
| Shielded  | shield absorbs until popped | electric                 | everything until popped |

Damage = `base × typeMultiplier(payload, modifiers) × adaptationMultiplier`. Pure C# table, unit-tested.
Placeholder visuals: coloured capsules or the alien FBX tinted per type. Enemies walk from the perimeter
(reuse `EnemyPerimeterSpawner` ring logic) toward the player's current pad; reaching it damages the station.

## Mothership adaptation

- `CombatLog` tracks damage dealt per payload and per modifier during a wave.
- Between waves: send the log to OpenAI → returns `{ counters: [...], taunt }` where counters come from a
  fixed list: `armor, shield, dodge, teleport, spread, rush, intercept, resist:<payload|modifier>`.
  Deterministic fallback (top-used primitive → matching counter) if the call fails or times out (3 s).
- Boss: every 20 s re-evaluates the log and gains `resist:<top primitive>` live, with a visible colour
  shift + taunt.

## AI + audio services

All HTTP via `UnityWebRequest` + async/await, one service class per provider, all behind interfaces with
a **Mock** implementation (offline mode toggle for no-Wi-Fi demos and tests).

- **OpenAI**
  - Transcription: mic clip → WAV → `/v1/audio/transcriptions` (model configurable, default a `*-transcribe` model).
  - Weapon interpreter: Responses/Chat API with `json_schema` strict structured output. Prompt includes the
    enemy types present in the current wave so the ship AI can nudge toward counters.
  - Mothership adaptation: same, different schema.
  - Model IDs live in a config asset, not code.
- **ElevenLabs**
  - TTS: Ship AI voice (friendly) and Mothership voice (menacing); two voice IDs in config.
  - Sound effects API: per-weapon fire + impact SFX from `sfxPrompt`, cached to disk by prompt hash.
    Placeholder built-in clips play until generated audio arrives; never blocks firing.
- Latency cover: "fabricating" hologram + ship AI filler line while calls run; old weapon stays usable.
  STT / LLM run sequentially; TTS + SFX run in parallel after the spec arrives.

## Secrets

`UserSettings/ArmoryKeys.json` (`{ "openai": "...", "elevenlabs": "..." }`), read in Editor/local builds
only. `UserSettings/` is added to `.gitignore`. Missing keys → mock mode + on-screen warning.

## Code layout

`Assets/Game/` with asmdef `Armory.Runtime`, `Armory.Editor`, `Armory.Tests` (EditMode).

- `Core/` pure C#: `WeaponSpec`, `WeaponSpecParser` (clamp + budget), `DamageTable`, `CombatLog`,
  `AdaptationRules` — all unit-tested.
- `VR/` rig, teleport, push-to-talk mic capture.
- `Weapons/` assembler + behaviour components + projectiles.
- `Enemies/` enemy, wave director, mothership.
- `AI/` OpenAI + ElevenLabs clients, mocks, config.
- `UI/` world-space ship AI subtitle panel + wave HUD.
- Editor menu **Armory → Setup SpaceStation Scene** adds/refreshes a single `Armory` root object in
  `SpaceStation.unity` (rig, pads, director, services) so scene merge conflicts can be resolved by
  re-running it.

## Testing

- EditMode: spec parsing/clamping/budget, damage table, adaptation fallback, WAV encoding.
- Play-mode smoke via desktop fallback with mock AI: 5 canned prompts each produce a firing weapon.
- Headset checkpoint after each build step (Quest Link).

## Build order (each step playable)

1. Rig + teleport pads + aliens walking in + hardcoded machine gun in SpaceStation.
2. WeaponSpec → assembler + behaviour components, driven by hand-written JSON / canned prompts.
3. OpenAI interpreter (typed text, then voice).
4. Enemy types, waves, mothership adaptation, boss.
5. ElevenLabs TTS + SFX, fabrication hologram polish.
