# Finalist push: latency, spectacle, and what judges actually see

Judging is originality, user experience, technical complexity and wow factor, demoed live in a few minutes.
Two facts shape everything below:

1. **Most judges will never put the headset on.** They watch the monitor. Whatever the mirror view shows *is* the
   project to them.
2. **The hook is not shooting aliens.** It is: *you say a sentence, and a weapon that did not exist is in your hands
   seconds later, and the enemy learns from it.* Everything should make that loop faster and more legible.

---

## A. Latency: speech → firing in under 2 s (currently ~3.6 s)

Measured today: transcription ≈1.7 s, weapon spec ≈1.9 s, sequential. Plan:

1. **Stream the transcript while the player talks.** Send audio to a streaming transcription model during the hold
   (or chunk every 700 ms) so the transcript is ready ~200 ms after the grip is released. Saves ~1.4 s.
2. **Stream the weapon spec.** Parse the JSON as it streams: as soon as `fireMode`/`element` arrive, start the
   fabrication animation with the right silhouette and colour; fill in numbers when the stream completes. The
   player perceives the build starting in ~600 ms.
3. **Parallel audio.** Voice line (ElevenLabs Flash, streaming) starts the moment the name is known; the SFX
   request runs alongside rather than after.
4. **Barge-in.** The player can interrupt ARIA by talking; her line stops immediately (feels conversational).
5. **Budget guard.** If any call exceeds 2.5 s, fall back to the local keyword interpreter, build immediately, and
   let the AI version swap in when it lands. The player never waits on the network.

Target: grip release → weapon in hand ≤ 1.2 s perceived, ≤ 2 s complete. Verified from Sentry traces
(`2026-09-20-sentry-observability.md`).

## B. Melee: swing the controller (high wow per hour of work)

The user asked for this and it is the strongest "VR moment" available.

- `fireMode: "melee"` in the weapon schema; the assembler builds a hilt plus a blade whose look follows the element
  (plasma ribbon, fire blade with heat haze, cryo shard, electric arc).
- Swing detection from controller velocity (`> 1.8 m/s`), damage scaled by swing speed, one hit per enemy per swing,
  chunky haptics and a slash trail; hits apply the element's debuff.
- **Deflect:** a well-timed swing at an incoming projectile bats it back — the crowd-pleasing moment.
- Enemies: Armored takes low melee damage (encourages mixing), Swarm gets cleaved in bulk.
- Voice examples: "give me a plasma katana", "a flaming greatsword that sets them on fire".

## C. Spectator view (what the judges watch)

A second camera rendered to the PC window, not a raw mirror:

- Third-person chase of the player, with the arena readable.
- Overlay: the live transcript ("YOU: shotgun that fires sticky mines"), the parsed spec chips appearing one by one,
  the blueprint image when it lands, and a hive panel showing what the mothership just learned.
- Kill-cam slow-mo on the wave's last kill and on the boss's death.
- This is also the demo video: record the spectator view, no extra work.

## D. Legible adaptation (the part that makes it a *system*, not a toy)

- Between waves the hive panel animates: "analysed: plasma 62% · homing 21%" → "grew: reflective plating,
  blink glands". Mothership voice reads it.
- Enemies visibly wear their counters (mirror scales, shield bubbles, blink afterimages), so the player *sees* why
  their old weapon stopped working.
- ARIA suggests a counter-strategy in the armory phase, which is what makes the conversation feel like collaboration
  rather than a command line.

## E. Demo structure (5 minutes, rehearsed)

1. 20 s: shuttle approach, story hook, ARIA introduces herself. (Sets stakes, hides loading.)
2. 40 s: tutorial condensed — move, translocate, one spoken weapon.
3. 60 s: wave 1-2, swarm; player invents an AoE weapon mid-fight.
4. 40 s: hive adapts on camera; armored wave punishes the old weapon; player sketches a railgun.
5. 60 s: boss assembles from the wreck; melee deflect moment; boss mimics the player's weapon.
6. 20 s: field report artefact (below) on screen while the team talks about the OpenAI/ElevenLabs/Sentry stack.

## F. Field report artefact (post-run, shareable)

At the end of a run, generate a one-page report: every weapon invented (name, blueprint image, spec chips), what
the hive adapted, and the kill stats — written by OpenAI, laid out as a web page the judges can open on their
phones. Cheap to build, and it leaves something behind after the demo.

## G. Cheap polish with outsized effect

- Starfield skybox + lighting pass (the windows currently show white daylight).
- Muzzle flash, impact sparks, dissolve deaths, hit-stop on big kills, controller haptics everywhere.
- Enemies use the alien model instead of capsules.
- ElevenLabs ambience bed + combat drone, ducked under voice.
- Pre-warmed cache so the five demo prompts are instant even on venue Wi-Fi.

## Order (highest judged value per hour)

1. Latency streaming (A) — the loop is the pitch.
2. Spectator view (C) — judges' entire perception.
3. Melee (B) — the wow moment.
4. Legible adaptation (D).
5. Polish (G), field report (F), demo rehearsal (E).

Sentry work runs alongside (it produces the evidence for A) and is written up in its own plan.
