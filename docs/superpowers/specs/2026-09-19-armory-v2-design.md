# Alien Armory v2 — voice fix, story, tutorial, armory phase, elements

Follow-up to `2026-09-19-alien-armory-design.md` after the first headset test.
Decisions from the user (2026-09-19): planning phase is untimed and player-started; drawing is a sketch board
combined with voice; smooth stick locomotion; pads teleport when you stand on them; elements with debuffs and a
balance budget; RPM/tuning within bounds; a coherent story for why you are here.

---

## 1. Voice capture (bug fix, highest priority)

**Evidence.** In the 16:08-16:10 headset session the game sent **no** transcription request (Editor.log), and the
user saw "● LISTENING", so the left-grip binding fires and recording starts. Measured: the Quest mic device
("Headset Microphone (Oculus Virtual Audio Device)") returns samples whose peak is 3e-5 (digital silence) while
`Microphone.GetPosition` advances normally; a laptop mic on the same code path records fine at the same requested
16 kHz, so the sample-rate mismatch is not the cause. Conclusion: Unity receives a live but silent stream from the
Oculus virtual device, and `EndRecording`'s two guards (`< 0.33 s`, trimmed `< 0.25 s`) drop it **without logging
anything or telling the player why**.

**Fix (defence in depth):**
1. **Make it visible.** Live input-level meter on the comms card while talking; after release the status says
   exactly what happened ("heard nothing (peak 0.00003)", "too short 0.2 s", "sent 1.8 s"). Every attempt logs
   device, duration, peak, RMS and decision.
2. **Make it recoverable.** If a capture is silent, immediately retry on the next device: preference order is
   (a) the device named in `AiSettings.MicDeviceContains`, (b) an Oculus/Quest/headset device, (c) Windows default,
   (d) any other. Remember the last device that produced audio, in PlayerPrefs.
3. **Make the guards fair.** Minimum length 0.2 s; trim relative to the clip's own peak (15%) instead of a fixed
   0.015; normalise gain to ~0.3 peak before upload (Quest mics run quiet); only reject when peak < 0.002.
4. **Diagnose on the spot.** Bridge/menu command `miclevels`: records 2 s from every device in turn and prints
   peak/RMS, so one run in the headset identifies the device that actually carries Quest audio.
5. **Document the Link setting.** If every device is silent, the cause is outside Unity (Windows mic privacy, or
   Link routing); the HUD says so and points at the setting.

**Acceptance:** in the headset, holding grip shows a moving level meter; releasing produces a transcript or a
specific reason; a single `miclevels` run names a working device.

---

## 2. Story frame

The game opens outside the station, not in it.

**Premise.** You are a fabricator-rated marine aboard the tender *Kestrel*. The *Kestrel* mines and refits derelict
hulks; this one, the refinery station **Vasa Reach**, went dark eight hours ago after a pirate wreck drifted into
its dock ring. The wreck carried a hive. The hive is a *learning* organism: it samples whatever kills it and grows
a counter within minutes, which is why a fixed loadout is useless and why the *Kestrel* sends a marine with a
fabricator and an AI that can design weapons on demand. Your job is to hold the refinery core until the *Kestrel*
can burn the hive out. ARIA is the station's own fabrication AI, half-corrupted and dryly amused by all this.

**Beats (skippable, ~60 s total):**
1. **Approach.** Cockpit of the shuttle, station ahead through the window, ARIA and *Kestrel* control talk over
   comms; establishes hive, adaptation, and the fabricator.
2. **Dock and corridor.** You step out, walk a short corridor (this is the tutorial), ARIA wakes up and calibrates
   your fabricator on the way.
3. **Atrium.** Doors open into the arena; the hologram core spins up; wave 1 is inbound.
4. **Between waves.** Hive taunts + ARIA banter carry the story; the final wave reveals the hive avatar assembling
   itself out of the pirate wreck.

All narration is OpenAI-written once, voiced by ElevenLabs, cached to disk, and subtitled.

---

## 3. Tutorial ("fabricator induction")

Runs during the corridor walk, before wave 1, and is skippable at any time.

- A schematic controller model (built from primitives, no asset needed) floats beside the relevant hand with the
  active button glowing and a label; a checklist panel ticks steps off as they are completed.
- Steps: look around → move with the left stick → snap-turn with the right stick → stand on a pad to translocate →
  fire at three training drones → hold grip and talk to ARIA → sketch on the board → say "ready" to start the wave.
- Each step waits for the player to actually do it (no timers), and ARIA comments when it is done.

---

## 4. Locomotion

- **Smooth movement** on the left stick, head-relative, 2.6 m/s, with a comfort vignette while moving.
- **Right stick** snap turn 45° (setting for smooth turn).
- **Translocator pads** replace aim-teleport: stand on a pad, its ring fills over 0.6 s, then you are moved to the
  pad's linked destination (ground deck ↔ upper gantries). Stepping off cancels. Pads glow when occupied and show
  where they lead by a thin arc of light between the pair.
- Arena stays bounded by the railing; walking off the deck is blocked by an invisible wall.

---

## 5. Armory phase (untimed, player-started)

Between waves the game enters **ARMORY**: no spawns, the core repairs slowly, and the round starts only when the
player says "ready"/"start the wave" or presses A.

- ARIA opens with a briefing: what the hive did last wave, which counters are now active, and a suggestion.
- **Conversation, not one-shot.** Requests keep a short transcript (last 6 turns). The model returns either just a
  reply (asking a clarifying question, proposing an idea) or a reply plus a finished weapon spec; it only builds
  when the player confirms. Push-to-talk still gates the mic.
- **Sketch board.** A holographic board on the player's left during ARMORY. Right trigger draws (stroke colour by
  element), buttons for undo/clear. The PNG is sent with the next request as vision input, so the sketch shapes the
  weapon, and it is also the reference image for the gpt-image blueprint.
- The wrist card gains a "tuning" row during ARMORY: adjust RPM / damage / projectile count within the budget
  (below) with the thumbstick or by voice ("more RPM, less damage").

---

## 6. Elements, debuffs and the balance budget

**Elements** (each is a damage type *and* a debuff):

| Element | Debuff | Effect |
|---|---|---|
| Kinetic | — | cheap, high RPM |
| Fire | Burning | damage over time + **weakened** (target deals 25% less damage) |
| Plasma | Melting | −30% enemy armour for 4 s |
| Electric | Shocked | chains; **stuns** briefly; pops shields |
| Cryo | Chilled | −50% speed, then **brittle** (+25% damage taken) |
| Corrosive | Etched | stacks; each stack −10% enemy max HP regen/armour |

**Budget.** Every weapon costs points: element (0-3), each modifier (1-2), RPM band, projectile count, damage band,
splash radius. The player has a **fabrication budget** that grows per wave (e.g. 6, 8, 10, 12, 15). The LLM is told
the budget and must fit it; Unity re-costs the returned spec, and if it is over budget it trims the cheapest
modifiers and tells the player what it cut ("dropped chain to fit the budget"). This is what keeps "make it
infinite damage" from working, and it makes tuning a real choice.

**Tuning within bounds.** RPM, damage, count and spread are sliders whose product is capped by the budget: raising
RPM lowers damage automatically, and the HUD shows the cost breakdown live.

---

## 7. Final boss ideas (for Astra to animate/implement)

The teammates' 28 m alien becomes the **Hive Avatar**, assembled from the pirate wreck. Ideas, roughly in order of
effort:

1. **Adaptive plating.** Every 20 s it grows plating against the damage type it has taken most (already
   implemented); plating is visible as coloured scales that shed when you switch element.
2. **Limb targeting.** Four weak points (two claws, sac, head crest). Destroying a claw disables one attack.
3. **Sweep attack.** A claw sweep across a deck section, telegraphed by a glowing floor arc — the player must
   translocate to another pad. Good use of the pad system.
4. **Spore burst.** Launches spore pods that hatch swarmers unless shot down within 5 s (rewards splash weapons).
5. **Acid pools.** Drool leaves a spreading pool that shrinks the safe area and forces movement.
6. **Shield choir.** Shielded escorts link a beam to the avatar; while linked it is immune, so you must break the
   beams (rewards electric/chain).
7. **Wreck grab.** Rips a chunk of the pirate wreck and hurls it at the core: shoot it apart mid-air or the core
   takes heavy damage.
8. **Final phase.** Hive Avatar mimics the player's most-used weapon and fires a corrupted version back at you.

Each maps to an animation clip + a telegraph + a damage window, so Astra can implement them independently.

---

## 8. Out of scope for now

- Merging RiddhRam's `weapon-system` runtime wholesale; instead we take its ideas (drawing-first, paper-cutout
  ammo) into the existing Armory pipeline. Team decides later which runtime ships.
- Multiplayer.
