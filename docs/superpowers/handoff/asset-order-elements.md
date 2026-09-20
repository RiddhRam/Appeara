# Asset order for Codex — elemental effects and weapon feel

Written for Codex. Claude owns the gameplay scripts; this is an art/asset order. Source assets by generating them
(Blender, procedural meshes, generated textures) or by finding suitably licensed free assets — record provenance
and licence for anything downloaded, the way the Hive Avatar art did.

The gameplay already applies these debuffs; right now they are communicated only by tinting the alien. Each item
below is what would make the effect *readable and satisfying* instead of just correct.

## Context: what the code already does

`Assets/Game/Core/StatusEffects.cs` drives everything. Elements and their debuffs:

| Element | Debuff | Mechanical effect | Current visual |
|---|---|---|---|
| Plasma | Burning + Melting | damage over time, +30% damage taken | orange tint |
| Cryo | Chilled + Brittle | 55% slower, +25% damage taken | blue tint |
| Electric | Stunned | stopped for 0.7 s, pops shields | yellow tint |
| Explosive | Stagger | brief stop | none |
| Kinetic | — | — | none |

Hooks available to attach visuals: `Enemy.ApplyStatus(payload, damage)`, `Enemy.Status.Burning/Chilled/Stunned(now)`,
`Enemy.Status.Tint(now)`, plus `Effects.Burst/Flash/Lightning` for one-shots.

## What to make (priority order)

### 1. Status effect VFX on the alien (highest value)
For each of burning, chilled, stunned, provide a **prefab that attaches to an enemy and lives while the debuff is
active**. Keep them cheap: a swarm wave can have 45 enemies with debuffs at once.
- **Burning:** small looping flame particles at 2-3 points on the body, warm light flicker, thin smoke ribbon.
- **Chilled:** frost shell (a slightly enlarged, frosted copy of the body silhouette works), ice shards at the feet,
  breath-fog puff on hit.
- **Stunned:** arcing electricity between two or three points on the body, plus a small ring of sparks above it.
- Each prefab: `Assets/Game/Art/Status/<Name>.prefab`, self-contained, no scripts required beyond particles; if a
  script is needed for the shell scaling, keep it in the prefab and note it.

### 2. Impact effects by element
One-shot prefabs played at the hit point, ~0.4 s each: `PlasmaImpact`, `CryoImpact`, `ElectricImpact`,
`ExplosiveImpact`, `KineticImpact`. Distinct colour and silhouette so the player can tell what they are firing from
the impacts alone.

### 3. Muzzle flashes
`MuzzleFlash_<element>` prefabs, ~0.1 s, scaled for a weapon held at arm's length in VR (they are seen at ~40 cm, so
keep them small and bright rather than large and soft).

### 4. Projectile trails
Trail/ribbon materials per element for `Projectile`: plasma (hot core, soft glow), cryo (pale, crystalline),
electric (jagged), explosive (smoky), kinetic (thin tracer).

### 5. Death effects
A dissolve or shatter for each enemy type, ~0.6 s. Swarm deaths should be cheap since dozens pop at once.

## Constraints

- URP, single-pass instanced VR: check materials render correctly in both eyes (no screen-space-only tricks).
- Target 90 fps on Quest Link with 45+ enemies alive; prefer GPU-cheap particles and avoid per-frame allocation.
- Match the existing palette: station cyan `#6BE0FF`, alien red `#FF5A6E`, amber `#FFB847`, deep navy background.
- Keep everything under `Assets/Game/Art/` with a short README noting source and licence per asset.

## Hand-back

When the prefabs exist, list their paths in the README and ping Claude; wiring them to the status hooks is a small
scripting change on this side. Do not modify gameplay scripts, `SpaceStation.unity`, or the weapon pipeline.
