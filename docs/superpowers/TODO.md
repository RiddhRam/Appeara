# Alien Armory — working TODO (2026-09-20)

Branch `armory-vr`. Commits are local until someone runs `git push origin armory-vr`.

## Done
- Core loop: speech → OpenAI WeaponSpec → assembled weapon → waves → mothership adaptation → adaptive boss.
- ElevenLabs ARIA + mothership voices, per-weapon generated SFX, gpt-image blueprint holograms.
- Station materials fixed (was all white), HUD redesign (IBM Plex + TextMesh Pro, glass panels, segmented gauges).
- **Voice capture fix** (commit `3fd5fcf`): mic diagnostics, live input meter, per-attempt reason, device fallback,
  `Armory → Mic Levels` probe. Transcription confirmed working in the headset.

## Recently done (this session)
- **Hive Avatar boss committed** (`2e25415`): Codex's art prefab + encounter prototype, plus a targeting fix.
  The bone-anchored organs sat inside the body collider, so 3 of 4 organs were unhittable; body is now a torso
  capsule, organs are pushed clear (`HiveTargets`), and body hits near an organ route to it. Ray-based tests added.
- **Untimed ARMORY phase** (`45c682f`): waves wait for "ready" / right A button; core repairs between waves.
- **Kestrel Drydock** (`aa5d755`): welcome screen + dev console (wave select, skip, restart, mic test, offline AI).
  Opens on launch and on left Y / M; controller ray + trigger to press.

## Blocked on animation (moved to the back, 2026-09-20)
- **Boss attacks: stomp + shockwave, fireball, sweeping laser, enrage.** Codex did NOT animate these; only claw
  sweeps, spore pods and wreckage exist, using imported clips. Scripting waits on Astra's clips and animation
  events - brief: [hive-avatar-animation-brief.md](handoff/hive-avatar-animation-brief.md). Once the clips land,
  the script side is: trigger -> wait for the named event -> spawn hazard -> re-enable movement.

## Known nits
- Claw labels read mirrored from the player's viewpoint ("RIGHT CLAW" appears on the player's left).
- Station architecture frames the boss fight awkwardly (arena proposal still open).
- Art prefab (`HiveAvatarVisual.prefab`) is still not what `EnemyFactory` instantiates; runtime builds its own
  plates/organs. Integration remains.

## Next up (ordered)
1. ~~**Armory phase timing**~~ done — remaining: multi-turn conversation during the phase — user feedback: waves arrive with no time to talk. Untimed pre-wave ARMORY state:
   no spawns, ARIA briefs, multi-turn conversation, wave starts only on "ready"/A. (Task 3 of armory-v2 plan.)
2. **Latency: speech → firing under 2 s** — stream transcription during the hold, stream the weapon spec and start
   the fabrication animation early, parallel voice/SFX, local fallback if a call exceeds 2.5 s.
3. **Spectator view** — third-person camera on the PC window with transcript, spec chips, blueprint and hive panel.
   This is what judges watch; doubles as the demo video.
4. **Melee weapons** — swing the controller: plasma/fire/cryo blades, damage by swing speed, haptics, deflect
   incoming projectiles. Requested by user.
5. **Locomotion** — smooth stick movement; teleport pads become stand-on translocators.
6. **Elements + debuffs + budget** — fire/burning+weakened, cryo/chilled+brittle, electric/shocked, plasma/melting,
   corrosive/etched; per-wave fabrication budget with re-costing, RPM/damage tuning within bounds.
7. **Legible adaptation** — hive panel animating what it learned; enemies visibly wear their counters.
8. **Sketch board** — draw during armory phase; sketch sent as vision input and seeds the blueprint.
9. **Tutorial** — fabricator induction with schematic controller visuals, step-by-step, skippable.
10. **Story frame** — shuttle approach to the derelict refinery, dock, corridor, atrium reveal (spec section 2).
11. **Sentry observability** — gateway service holding keys + tracing/logs/AI monitoring/profiling/uptime.
12. **Polish** — starfield skybox, alien models for enemies, muzzle flash/impact/dissolve, ambience bed, pooling,
    pre-warmed demo cache.
13. **Boss continuation for Claude** — see [CLAUDE_HANDOFF.md](../../CLAUDE_HANDOFF.md). Codex delivered a
    standalone visual prefab + animation controller and preserved an earlier gameplay prototype. Integrate
    the art prefab, fix left-claw collider occlusion, then continue the encounter scripting. Not final/polished.

## Mobility as a fabricated item (user, 2026-09-20)
Movement speed should be something you *spend* on, not a free stat:
- A fabricable **mobility item** (thruster gauntlet / jet harness / grav-boots) that raises sprint speed, adds a
  dash, or shortens pad charge time. It costs from the same per-wave fabrication budget as weapons, so taking
  speed means giving up damage or rate of fire.
- Say it out loud like any weapon: "give me thrusters", "something that makes me faster", "boots that dash".
- Schema: add `kind: weapon | mobility` to the spec, with mobility fields (sprintBonus, dashDistance,
  dashCooldown, padChargeScale) and its own budget costs.
- Balance hook: mobility competes with firepower, and the hive's `rush` counter makes speed more valuable, so the
  choice shifts by wave.
- Base sprint stays free (left stick click) so the player is never stranded; the item makes it meaningfully faster.

## Weapon primitives to consider (user, 2026-09-20)
Feasibility notes against the current controller scheme (right trigger fire, left grip talk):
- **Beam / laser** — already implemented (`fireMode: beam`), continuous while trigger held. Keep.
- **Explosion / AoE** — implemented as `splash` + explosive payload; could be promoted to its own primitive with
  a thrown/placed variant.
- **Apply status effect + enemy tint** — planned in item 6; tint enemies by status (red burning, blue frozen,
  yellow shocked) so the debuff is readable at a glance. Easy win, high legibility.
- **Melee (swing)** — item 4; needs velocity-based swing detection, no new buttons.
- **Bow / charged shot** — feasible: hold trigger to draw (haptic ramp), release to loose; pairs well with the
  left hand as the bow hand (two-handed pose from both controller positions).

## Open decisions
- Boss body: teammates' 28 m alien as the Hive Avatar (assumed yes).
- Which weapon runtime ships: this one or RiddhRam's `weapon-system` branch.
- Whether to add the gateway service (needed for the Sentry story, also removes keys from builds).

## New demo requests (2026-09-19, Codex continuation)
- Audio output now works, confirmed by the user. Transcription also works.
- **Fabrication must have a cost.** Give each wave a finite energy allowance. Price damage, RPM, projectile
  count, area, and modifiers against the same budget. Show cost and remaining energy before confirming a build.
  Replacing a weapon returns only part of its cost; repairs/upgrades are cheaper than a full replacement.
  Keep a free basic rifle so the player cannot get stuck. Refill at the next ARMORY phase; award a small bonus
  for destroying boss organs. Exact prices/refunds need playtesting. Build on item 6, not a second currency system.
- **Boss stomp / ground attack.** Telegraph the impact, then send an expanding shockwave across the deck.
  Provide a pad escape route; do not require jumping with the current controls.
- **Boss fireballs.** Visible, dodgeable projectiles with a clear windup. Consider shooting them down and later
  melee deflection; distinguish their color/trail from player shots.
- **Boss laser.** Charge the mouth/crest, show the sweep path, then fire a sustained sweeping beam. Leave a safe
  sector and recovery window. Tie the attack to a breakable organ so targeting changes the fight.
- **Dedicated boss arena (proposal).** Move the finale to an open cargo deck or exterior docking platform.
  Keep the center clear, place the boss at one end, use low cover and perimeter teleport pads, and put a small
  core objective off-center. The current station hologram obstructs sightlines. Review arena layout with the
  team before replacing their map; a separate runtime-built finale can preserve the station for normal waves.
- **Player health (proposal).** There is currently no player HP/death. All danger reduces core integrity and
  a core breach restarts the wave. Add player shields/HP for claws, stomp, fireballs and lasers; keep wreckage
  and swarmers as core threats. Give hits a brief grace period and clear HUD/haptics, without shaking the VR
  camera. Define player-death/retry behavior and balance before changing this rule.
