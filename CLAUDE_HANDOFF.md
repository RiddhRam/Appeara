# Codex to Claude: demo art handoff

This supersedes the earlier boss handoff. Both Claude JSONL conversations, their queued user messages, memory, the asset orders and current git history were reviewed. Audio works per the user. Codex's scope remains art/animation; Claude owns gameplay scripting.

Branch: `armory-vr`. Origin was pulled and merged. The remote sketch-board collider fix conflicted with the newer intentional removal of drawing; the removal was retained. Art and this handoff are being committed and pushed separately from local Unity settings/logs/recovery files. No co-author trailers.

## Delivered

- Five textured URP enemy prefabs: `Assets/Game/Art/Enemies/Authored/{Grunt,Swarm,Armored,Fast,Shielded}Visual.prefab`, assigned in `Assets/Game/Enemies/Resources/EnemyVisuals.asset`.
- AlienMonster humanoid animation retargeting; supplied robot motion; rigid hover for static assets. Wendy has a separately authored mesh with lowered arms, not a fabricated skeletal rig. Original downloads remain intact. See [enemy asset README](Assets/Game/Art/Enemies/README.md).
- Boss stomp, fireball, sweeping laser, enrage and settled death, plus grounded imported locomotion/claw/bite clips. Existing skeleton/controller retained; `Mouth_Socket` added. See [exact animation contract](Assets/Game/Art/HiveAvatar/ANIMATION_HANDOFF.md).
- 27 art-only prefabs: three statuses, five impacts, five muzzle flashes, five trails, five enemy death accents and four boss effects. See [effect contract](Assets/Game/Art/Effects/README.md).
- Editor builders and repeatable rendered preview sheets. No gameplay scripts, weapon pipeline, audio or SpaceStation changes in this art commit.

## Claude: implement next

1. Instantiate authored ordinary-enemy visuals through `EnemyFactory` and the table. Preserve existing collision/health rules. **Fix `Enemy.SetTint` first:** replacing `sharedMaterial` discards imported textures; use MaterialPropertyBlock/tint state while excluding effect renderers. Select the correct Animator when a prefab has both hover and skeletal animators.
2. Add event receivers on the boss Animator object and forward `OnStompImpact`, `OnFireballRelease`, `OnLaserStart`, `OnLaserEnd`, `OnAttackRecovered` to the encounter. Schedule telegraph -> hazard -> recovery; gate by organ state and death. New attacks are assets, not playable attacks yet.
3. Extend boss destruction from `Enemy.Die`'s 3.5 seconds to at least the new 4.5-second death plus blending. Clear pending attack triggers on death. Trigger Enrage once; set Enraged bool for faster locomotion. Optional laser loop requires explicit entry/exit ownership.
4. Attach/reuse statuses and wire impacts, muzzle flashes, trails and death accents. Respect loop cleanup and one-shot lifetimes. Particle death accents do not dissolve the source mesh.
5. Integrate existing `WeaponBudget` into fabrication and HUD. Display cost/remaining energy, apply the existing trimming result before fabrication, define replacement refunds and refill in ARMORY. Retain a free basic fallback. Mobility purchases compete with weapons in the same budget; this remains a TODO.
6. Re-test grounded boss scale, organ collider sightlines, hit routing and movement in the eventual arena. Current collider rules were deliberately preserved.
7. Run headset checks: both-eye transparency, animations/blends, ground-level telegraph readability, VR comfort, 45-enemy performance. No measured 90fps claim is made here.

## Arena and damage answers

An open cargo deck or docking platform is a better fit for the large stomp and 120-degree laser than the obstructed atrium. Keep the middle clear, put low cover and pads at the perimeter, retain safe sweep sectors, and place the core off-center. This is a layout proposal, not a replacement map delivered in this art pass. SpaceStation remains untouched.

The previous handoff's claim that there is no player health is stale. `PlayerVitals` and the shield HUD now exist; enemy proximity drains shields and a downed player gets a warning. There is still no personal death/retry transition in that path. Existing boss claw misses and wreckage damage **core integrity** (`HiveAvatar.Sweep`, `HiveThreat`). Claude should route new direct boss attacks to player shields and define the downed/retry behavior; keep wreckage and swarmers as core pressure.

Other stale items: Claude already integrated the boss art and fixed organ/body ray occlusion, added locomotion/untimed ARMORY, removed drawing, added sword/bow logic, spectator work, budget rules, health bars and XR startup ownership fixes. Do not repeat the old integration/occlusion tasks as though absent.

## Provenance and limits

Normal models came from the user's staged `ThirdParty/EnemyModels` downloads. The source license records remain missing; no claim of cleared redistribution is made. No new models were downloaded this turn. Boss retains the existing AlienAnimal source. VFX textures and Wendy pose are authored locally. Source importer warnings included duplicate head bones, recalculated Wendy normals and GunBot material extraction names; authored prefabs use dedicated materials.

Baseline: 113 EditMode tests passed. Final validation results and preview links are recorded in `docs/superpowers/handoff/demo-art-validation.md`.
