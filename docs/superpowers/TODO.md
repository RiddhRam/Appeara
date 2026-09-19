# Alien Armory — working TODO (2026-09-20)

Branch `armory-vr`. Commits are local until someone runs `git push origin armory-vr`.

## Done
- Core loop: speech → OpenAI WeaponSpec → assembled weapon → waves → mothership adaptation → adaptive boss.
- ElevenLabs ARIA + mothership voices, per-weapon generated SFX, gpt-image blueprint holograms.
- Station materials fixed (was all white), HUD redesign (IBM Plex + TextMesh Pro, glass panels, segmented gauges).
- **Voice capture fix** (commit `3fd5fcf`): mic diagnostics, live input meter, per-attempt reason, device fallback,
  `Armory → Mic Levels` probe. Transcription confirmed working in the headset.

## Next up (ordered)
1. **Armory phase timing** — user feedback: waves arrive with no time to talk. Untimed pre-wave ARMORY state:
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
13. **Boss handoff for Astra** — Hive Avatar hooks, telegraph API, attack list (spec section 7).

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
