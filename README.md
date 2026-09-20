# Appeara

> **Stop scrolling. Start creating.**

Appeara is a VR survival game where imagination is your loadout. Describe or sketch a weapon, watch it become a balanced, playable creation, and use it to defend the heart of a besieged space station. But the alien hive is learning: every invention teaches the Mothership how to fight back.

Built for [Hack the North 2026](https://hackthenorth2026.devpost.com/).

## The Spark

**How Appeara initialized**

DOOM SCROLLING.  
SLEEPING.  
MARATHONING.  
MATCHA.

We've all become the same person: the same habits, the same life. How boring.

As we spend more time consuming digital content, games can start to feel repetitive and predictable. We wanted to make something that asks players to think creatively instead of following a predetermined path.

**That is what created Appeara.**

We wanted to bring imagination back into gaming by letting players create the weapons they actually imagine.

## Powered On and Creative

### What does Appeara do?

Appeara is a VR survival game where players use their imagination to create weapons and defend the heart of a space station.

#### Weapon Creator

Players can describe and sketch almost any weapon they can imagine. OpenAI interprets their input and turns it into a validated `WeaponSpec`; Unity then assembles a balanced weapon that can actually be used in-game.

OpenAI can also generate a holographic blueprint and a constrained parts list, allowing Unity to reconstruct the invention as an original 3D object. Fabrication is deliberately limited, so players must choose their ideas wisely.

#### Adaptive Enemies

The enemies in Appeara adapt to the weapons players create. After analyzing a player's weapon, the Mothership develops defensive and tactical countermeasures. Once the hive adapts, players must change their strategy and create something new to survive.

#### Dynamic Audio

ElevenLabs gives every invention its own sound and personality. It generates custom firing sounds based on a weapon's behaviour; ARIA introduces new weapons, while the Mothership responds with dynamic dialogue and taunts. Every battle can sound different because each player creates something different.

## YouTube Tutorial, Google Search, Code, Repeat

### How we built Appeara

We built Appeara in **Unity 6** with Meta XR and OpenXR, bringing together VR interaction, natural-language input, generative AI, procedural gameplay, dynamic audio, and real-time enemy adaptation.

| Technology | How we used it |
| --- | --- |
| **Unity** | The real-time VR game: interaction, procedural weapon assembly, combat, effects, UI, physics, and enemy waves. |
| **OpenAI API** | Speech-to-text, structured weapon specifications, holographic blueprint art, and constrained weapon-part descriptions. |
| **ElevenLabs** | AI-generated weapon sound effects, ARIA's voice, and Mothership dialogue. Audio is cached to avoid duplicate generation. |
| **Sentry** | End-to-end traces, structured logs, error monitoring, and performance profiling across Unity and our server-side gateway. |
| **Blender** | Preparing and iterating on 3D assets for the space-station setting and enemy presentation. |
| **Codex** | Our development teammate for interconnected gameplay work, debugging rendering/scaling/animation/targeting issues, and adding unit tests. |

The AI pipeline is intentionally constrained. Rather than treating an LLM response as game code, we request structured output, validate it, enforce a gameplay budget, and fall back to local rules when a provider is unavailable.

```text
voice + sketch + battle context
              ↓
        OpenAI WeaponSpec
              ↓
  Unity validation + balance budget
              ↓
 procedural weapon + blueprint + sound
              ↓
 Mothership counter-analysis → new player strategy
```

We keep provider keys out of the Unity build with a small FastAPI gateway. It forwards only supported OpenAI and ElevenLabs requests while continuing Sentry trace context, so a fabrication can be inspected as one end-to-end flow.

### Debugging with Sentry

Sentry helped us find issues that were hard to see from gameplay alone:

- It revealed where microphone input stopped flowing through the voice-to-weapon pipeline.
- It helped diagnose an enemy-spawn failure in particular waves.
- Traces showed counter generation completing after the enemy counter timer. We now begin LLM analysis as soon as a weapon is fabricated and hold its result until the timer expires.
- Performance traces exposed frame-time spikes from repeated enemy and projectile creation/destruction, prompting object pooling to reduce overhead.

Telemetry deliberately excludes raw audio, transcripts, prompts, and credentials.

## The Urge to Break Our Laptops

### Challenges we ran into

The hardest part was getting many real-time systems to communicate reliably under a hackathon deadline: VR, Unity, microphone capture, AI-generated weapons, dynamic audio, enemy adaptation, and external services.

We had to ensure that creative OpenAI output was still structured enough for Unity to turn into playable objects. We also had to make the experience resilient: slow model calls, failed requests, quiet microphones, and generated content outside the game's constraints all need a graceful fallback.

Timing mattered too. Counter generation could finish after the hive was meant to adapt, and repeatedly creating/destroying enemies and projectiles created performance pressure. Instrumentation made these bottlenecks visible; validation, early analysis, caching, and pooling made the game more reliable.

## The Top of the Hill

### What we're proud of

We combined VR, Unity, OpenAI, ElevenLabs, Sentry, Blender, and Codex into one playable experience during a hackathon.

Most of all, we are proud that a player's own idea becomes part of the game. Appeara does not just offer a fixed list of weapons: it makes creativity a mechanic. The adaptive enemy system then asks players to keep inventing rather than relying on one optimal strategy.

## Sunday Morning

### What we learned

We learned that building with AI is more than prompting a model. A real-time game needs structured outputs, validation, sensible constraints, timing, privacy, observability, and reliable fallbacks.

We also learned how much can be accomplished when a team connects different technologies around one clear experience: turning imagination into gameplay.

## What's next for Appeara

We want to grow Appeara into a larger creative game world with more weapon archetypes, enemies, environments, puzzles, and ways for players to interact through voice and drawing. We also want to explore multiplayer, where players can create and test inventions together.

**We want players to stop scrolling and start creating.**

## Running locally

### Unity client

1. Open this folder in **Unity 6.3.13f1**.
2. Install the packages listed in [`Packages/manifest.json`](Packages/manifest.json), including Meta XR, OpenXR, URP, and Sentry.
3. Create `UserSettings/ArmoryKeys.json` (this file is intentionally not committed):

   ```json
   {
     "openai": "YOUR_OPENAI_KEY",
     "elevenlabs": "YOUR_ELEVENLABS_KEY",
     "sentry": "YOUR_SENTRY_DSN",
     "sentryEnvironment": "development"
   }
   ```

4. Open the game scene and press Play, or build to a supported Meta XR target.

The game can run in offline mode with local rule-based fallbacks if external keys are not configured.

### Optional gateway

For a build or shared demo, run the credential gateway in [`Services/armory-gateway`](Services/armory-gateway). It keeps OpenAI and ElevenLabs provider keys off the client. See its [setup notes](Services/armory-gateway/README.md).

## Repository map

```text
Assets/Game/                 Unity gameplay, VR, weapon, enemy, and AI code
Assets/Game/AI/              OpenAI, ElevenLabs, and Sentry integrations
Assets/Game/Core/            Weapon schemas, validation, balance, and game rules
Assets/Game/Enemies/         Waves, adaptations, and the Hive Avatar
Services/armory-gateway/     Optional FastAPI credential and tracing gateway
```

## Team

Built with curiosity, caffeine, and a refusal to accept a boring loadout.
