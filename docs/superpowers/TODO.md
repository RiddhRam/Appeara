# Alien Armory working TODO

Branch: `armory-vr`. Latest art pass: [Claude handoff](../../CLAUDE_HANDOFF.md).

## Confirmed done

- Voice/transcription/audio working per user; speech -> weapon loop and adaptive waves.
- Untimed pre-wave ARMORY phase, smooth locomotion/sprint, translocator pads.
- Boss art integration, organ/body targeting fix and ray tests; original claw/spore/wreckage encounter.
- Element status rules, health bars, player shield state, sword/bow archetypes, fabrication budget rules.
- Drawing removed by user request; spectator and XR ownership fixes exist.
- Five ordinary monster visual prefabs with textures/controllers and editable EnemyVisuals table.
- Boss stomp/fireball/laser/enrage/death animation assets, sockets and event contract.
- Elemental status/impact/muzzle/trail/death art and boss effect prefabs.

## Claude's demo priorities

- [ ] Wire normal enemy prefabs into EnemyFactory; preserve textures during tint/status/hit flashes.
- [ ] Wire boss events and new attack scheduling, collision and damage. Art is ready; attacks are not playable yet.
- [ ] Extend death lifetime to cover 4.5-second clip; guard/clear attack triggers on death.
- [ ] Wire VFX with lifetime cleanup/pooling; profile the 45-enemy wave in headset.
- [ ] Finish fabrication friction: finite per-wave energy, previewed cost/remaining balance, budget trimming, refunds, free fallback. Reuse existing WeaponBudget rules.
- [ ] Fabricable mobility upgrade competing with weapon budget; base sprint remains free.
- [ ] Decide player-down/retry behavior. Shields exist; direct boss attacks currently still damage the core. Route new stomp/fireball/laser to the intended target explicitly.
- [ ] Review open cargo deck/docking-platform finale with clear center, low cover, perimeter pads, safe laser sectors and off-center core. Existing station is preserved.
- [ ] Verify collider/socket alignment after grounded clips; death finale and enrage; headset readability and stereo VFX.
- [ ] Capture source/license information for supplied enemy downloads before external distribution.

## Remaining product work

- Multi-turn ARMORY conversation and lower speech-to-firing latency.
- Judge-facing spectator demo capture and readable adaptation feedback.
- Tutorial/induction and story approach sequence.
- Gateway/key handling and observability if required for the demo.
- Cache prewarm, pooling, ambience and final headset performance pass.
- Later boss ideas: shield choir, acid pools, weapon mimic. Do not expand these before the primary attacks play well.

The earlier TODO's sketch-board, missing boss-prefab integration, missing locomotion and absent-player-health entries were stale. See the handoff for precise current behavior and remaining work.
