# Elemental and boss effect assets

Original procedural art authored for Ink-Bound. All sprite textures are generated
from analytic shapes and deterministic Unity noise by `ElementalAssetBuilder`;
there are no downloaded sources, third-party licence requirements, or external
packages. These assets follow the project's licence.

Regenerate with **Armory > Art > Build Elemental Effects**, or call
`Armory.Editor.ElementalAssetBuilder.Build()`. Existing asset GUIDs are preserved.
The builder never edits gameplay scripts or scenes. Generated prefabs have no
custom components, collision, realtime lights, audio, damage, or targeting.

## Asset contract for Claude

Paths below are relative to `Assets/Game/Art/`. One Unity unit means one metre.
The element names are `Plasma`, `Cryo`, `Electric`, `Explosive`, and `Kinetic`.

| Path | Placement and ownership |
| --- | --- |
| `Status/Burning.prefab` | Parent at enemy foot origin. Three body flame emitters and a thin smoke emitter; 38 particles maximum. |
| `Status/Chilled.prefab` | Parent at enemy foot origin. Suspended frost facets, foot crystals and cold mist; 38 particles maximum. |
| `Status/Stunned.prefab` | Parent at enemy foot origin. Three animated arc sprites and a spark halo; 30 particles maximum. |
| `Effects/<element>Impact.prefab` | Place at hit position. Particles last 0.4 s; root self-destroys at 0.5 s. |
| `Effects/MuzzleFlash_<element>.prefab` | Parent to muzzle with local +Z down barrel. Approximately 4.5 cm particles, 0.09 s flash; root self-destroys at 0.16 s. |
| `Trails/<element>Trail.prefab` | Parent to moving projectile. Clear trail after teleport/spawn if pooling. Controller owns cleanup. |
| `Trails/<element>Trail.mat` | Available separately for existing projectile TrailRenderer. Material expects vertex colour. |
| `Effects/Death_Grunt.prefab` | Red fragments, 0.6 s. Place at enemy foot origin; self-destroys at 0.7 s. |
| `Effects/Death_Swarm.prefab` | Ten small fragments, 0.6 s. Place at foot origin; self-destroys at 0.7 s. |
| `Effects/Death_Armored.prefab` | Angular electric-textured red fragments, 0.6 s; self-destroys at 0.7 s. |
| `Effects/Death_Fast.prefab` | Faster smoky red fragments, 0.6 s; self-destroys at 0.7 s. |
| `Effects/Death_Shielded.prefab` | Cyan fragments and shield ring, 0.6 s; self-destroys at 0.7 s. |
| `Effects/BossShockwave.prefab` | Place on ground at stomp origin. Horizontal ring grows to 12 m diameter over 1 s; self-destroys at 1.2 s. |
| `Effects/BossFireball.prefab` | Looping hot core and world-space embers. Move root as projectile; controller owns cleanup. |
| `Effects/BossLaserCharge.prefab` | Looping inward-moving sparks. Parent to firing socket; controller owns cleanup. |
| `Effects/BossLaserBeam.prefab` | Local-space LineRenderer, origin to +Z 8 m. Set endpoint to raycast hit in local coordinates; controller owns visibility and cleanup. |

Status prefabs assume a roughly 1.6 m enemy; scale the root uniformly to fit each
enemy kind. They loop until explicitly stopped. For a graceful expiry, call
`Stop(true, ParticleSystemStopBehavior.StopEmitting)` on **each** child particle
system, then destroy or return the effect after its longest remaining lifetime
(Burning 0.8 s, Chilled 1 s, Stunned 0.25 s). Their container has no particle
system. Remove immediately on enemy destruction. Do not repeatedly instantiate
a status every frame: keep one instance per active status and reuse it.

One-shots use a lifetime-only root particle system with `stopAction = Destroy`;
call Play with children enabled after reusing a pooled instance and replace the
Destroy policy if integrating with a pool. Status loops and projectile visuals
have no self-destruction timer because gameplay determines their duration.

## Visual and performance limits

Uses URP `Particles/Unlit`, standard particle, trail, and line renderers,
transparent sprite materials, and no screen-space/depth sampling. These support
the standard URP stereo path, but **both-eye rendering and a 90 fps Quest Link
budget require headset validation**. Forty-five enemies simultaneously carrying
all three statuses can budget 4,770 particles; consider displaying only the
dominant status or lowering emission on distant enemies after profiling.

The burning glow is emissive-looking particle colour, not a light flicker. The
chilled effect uses facets instead of a model-specific frost shell; attaching an
actual matching shell requires each enemy mesh and renderer integration. Arc
sprites flicker near three body positions; they do not dynamically connect bone
endpoints. Deaths are particle breakup accents, not shader dissolves or fractures
of the enemy mesh. Claude must hide the dead model and spawn the appropriate
effect. Cryo impact provides the hit puff; the status mist itself loops.

Boss effects are telegraphs/visuals only. Shockwave damage timing must follow the
growing radius; the fireball needs collision and damage; laser charge and beam
need attack sequencing, raycasts and safe telegraph timing. No boss controller is
modified by this asset pass.

Run the edit-mode `Armory.Tests.ElementalAssetTests` after generation to check
prefab availability, materials, particle budgets, one-shot lifetimes and cleanup.
