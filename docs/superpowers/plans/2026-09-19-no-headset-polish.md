# Alien Armory — work that doesn't need the headset

Goal: make the demo more reliable and better-looking while the headset is unavailable. Everything below can
be built and verified in the Editor (desktop mode + Unity MCP captures); items marked (feel-test) need a
headset pass afterwards.

Ordered by value for the demo.

## 1. Demo safety: pre-warm the AI caches (high)
- Editor menu **Armory → Pre-warm Demo Cache**: runs the 5 canned prompts through OpenAI once and caches the
  resulting WeaponSpec JSON, gpt-image blueprints, ElevenLabs SFX and ARIA/mothership voice lines on disk.
- Runtime: canned prompts (keys 1-5 / a wrist "presets" list) load instantly from cache; live voice still
  goes to the APIs.
- Why: venue Wi-Fi is the biggest live-demo risk; blueprints take ~20 s uncached.
- Verify: disconnect network, run the 5 presets, all weapons/blueprints/voices appear.

## 2. Space outside the windows + lighting pass (high)
- Procedural starfield/nebula skybox shader (stars, faint nebula, a distant planet) replacing the white
  default sky visible through the station windows; set it as the scene skybox from ArmoryGame at runtime so
  the scene file stays untouched.
- Tone the imported directional lights down, add 3-4 coloured accent point lights around the arena, tune
  bloom threshold so holograms glow without washing out the floor.
- Verify: before/after captures from the player position and the overhead observer.

## 3. Real alien bodies (high)
- Use the AlienAnimal FBX (already rigged + animator controller) for Grunt/Armored/Fast, scaled per type and
  tinted; keep primitives for Swarm (readability) and the shield bubble for Shielded.
- Boss: needs a team decision — either reuse the teammates' 28 m alien placed in the scene (walk it in) or
  spawn a scaled copy. Until decided, the boss stays a sphere.
- Verify: colliders still hit (surface-hit/enemy-hit counters via bridge `state`), animation plays.

## 4. Combat feel (medium, feel-test)
- Muzzle flash + light pop, impact sparks by payload, dissolve-out on death, projectile/effect pooling.
- OpenXR haptics on fire/hit/fabrication complete (right controller), coded now and tuned on the headset.

## 5. ElevenLabs ambience (medium)
- Generate and cache a looping station ambience bed and a low combat drone via the sound-generation API;
  duck under voice lines. Strengthens the ElevenLabs prize story (voice + SFX + ambience all generated).

## 6. Performance pass (medium)
- Profile a swarm wave with the Unity profiler through MCP; pool projectiles/popups/bursts; target a stable
  90 fps on Quest Link (11 ms CPU frame).

## 7. Mic affordance (small)
- Live input-level meter on the comms status while the grip is held; "I heard: …" confirmation line.

## 8. Judge-facing material (medium)
- README + Devpost draft: what the OpenAI API does (structured weapon specs, mothership adaptation,
  transcription, gpt-image blueprints), what ElevenLabs does, architecture diagram, 5-minute demo script with
  fallback lines if the network drops.

## Open team decisions
- Boss body: teammates' giant alien vs placeholder.
- RiddhRam's `weapon-system` branch vs the Armory weapon assembler: pick one for the demo before merging.
