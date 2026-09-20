# Appeara working TODO

Branch: `armory-vr`. Art handoff: [Codex to Claude](../../CLAUDE_HANDOFF.md) and
[boss animation contract](../../Assets/Game/Art/HiveAvatar/ANIMATION_HANDOFF.md).

## Done and wired

- Voice -> weapon loop, adaptive waves, untimed armory phase, locomotion/sprint, translocator pads.
- Element status rules, sword/bow archetypes, health bars, player shields.
- All delivered art is now reachable from the runtime:
  - `EffectLibrary` + `ArtVfx` index and pool the 19 authored particle prefabs.
  - Monster models spawn per kind, fitted and grounded; tint goes through a property block so textures survive.
  - Per-payload muzzle flashes and impacts on every shot, procedural fallback when a key is missing.
  - Boss stomp/fireball/laser/enrage driven off the authored animation events, with telegraphs and cleanup.
- Boss attacks damage the player's shields, not the core. Going down restarts the wave.
- Finale is fought on `BossArena`, an open docking deck; the refinery is preserved and restored on restart.
- Fabrication spends per-wave energy; ARIA reads the trim aloud and the wrist card shows the draw.
- Rebranded to Appeara (product name, welcome-screen wordmark, gateway).

## Needs the headset - nobody can close these from the desktop

- [ ] **Do the animation events actually fire in play mode?** Everything is verified statically. If dispatch
      fails the console prints a `WaitFor` warning naming the beat; that warning existing is the test.
- [ ] Laser pitch. The authored sweep keeps the head level, and a level beam from a mouth ~12 m up never reaches
      the deck, so the pitch is solved against the deck instead. Deliberate deviation; needs eyes on.
- [ ] `Mouth_Socket` orientation and boss facing during wind-ups (visual only; damage is position-based).
- [ ] Frame rate on the 45-enemy swarm wave and during the beam. Swarm/Fast already cast no shadows.
- [ ] Walk-cycle speed vs gameplay speed (nothing syncs them, feet may skate).
- [ ] Hit flash readability through a dark texture, and both-eye particle readability.
- [ ] Whether MOVE/DOCK panel placement feels right in VR.
- [ ] Play-mode crash: confirm the XR double-init fix holds.

## Remaining product work

- [ ] Fabricable mobility upgrade competing with the weapon budget; base sprint stays free.
- [ ] Multi-turn armory conversation and lower speech-to-firing latency.
- [ ] Tutorial/induction and the story approach sequence. No title card anywhere except the welcome panel.
- [ ] Judge-facing demo capture. The spectator overlay was removed; decide what replaces it, if anything.
- [ ] Capture source/license information for the supplied enemy downloads before external distribution.
- [ ] Later boss ideas: shield choir, acid pools, weapon mimic, `LaserSweepLoop` extended beam. Do not expand
      these until the primary attacks have been seen working in a headset.
