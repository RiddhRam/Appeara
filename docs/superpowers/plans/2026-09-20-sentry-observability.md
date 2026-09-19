# Sentry observability plan (prize: "Best Use of Sentry")

The prize wants **two or more products beyond error monitoring**, and evidence that the data actually changed the
build. We have a real story already: the voice bug that ate the player's speech was invisible precisely because
nothing was instrumented. This plan makes that class of failure impossible to miss again.

## Why Sentry fits this project (not decoration)

The weapon pipeline is a distributed chain with four hops and a hard latency budget:

```
player speech → Unity capture → transcription (OpenAI) → weapon spec (OpenAI)
                                       ↘ sound effect (ElevenLabs) ↘ voice line (ElevenLabs) ↘ blueprint (gpt-image)
```

When a weapon takes 6 s instead of 2 s, nobody can say which hop was slow. Tracing answers that directly, and the
answer drives the latency work in `2026-09-20-finalist-push.md`.

## Products used

1. **Tracing (distributed, Unity → service → model providers).** One transaction per fabrication, `armory.fabricate`,
   with spans: `mic.capture`, `openai.transcribe`, `openai.weapon_spec`, `unity.assemble`, `elevenlabs.sfx`,
   `elevenlabs.tts`, `openai.image`. Unity starts the trace and forwards `sentry-trace`/`baggage` to the key
   service (below), so the model calls appear as children of the in-game action.
2. **Logs.** Structured logs for exactly the things that failed silently before: mic device chosen, capture peak/rms
   and verdict, schema-validation trims ("dropped chain to fit budget"), fallback to the offline interpreter,
   ElevenLabs cache hit/miss. Each log carries the trace id, so a slow or broken weapon links to its trace.
3. **AI agent monitoring.** Instrument the OpenAI calls in the key service so model, tokens, cost and structured
   output failures/refusals are tracked per fabrication. This makes prompt regressions visible (e.g. a prompt change
   that doubles tokens, or a spike in refusals from the image safety filter).
4. **Profiling (Unity).** Frame profiles during swarm waves; we already suspect per-shot allocations (projectiles,
   popups, bursts create GameObjects). Profile → pool → measure.
5. **Uptime monitoring.** A check against the key service's `/health`, so a dead venue network or a crashed service
   is known before the demo, not during it.

## Work

### S1 — Key service (also fixes a real weakness)
Small FastAPI/Flask service (`Services/armory-gateway/`) that holds the OpenAI/ElevenLabs keys and proxies
`/transcribe`, `/weapon`, `/adapt`, `/sfx`, `/tts`, `/image`, plus `/health`. Unity stops shipping keys in a build.
Sentry Python SDK initialised with `traces_sample_rate=1.0`, logs enabled, OpenAI integration on.
Unity keeps direct-call mode as a fallback flag so the demo still runs if the service is down.

### S2 — Unity SDK
`io.sentry.unity` package; init with release/environment, `AutoSessionTracking`, profiling enabled, breadcrumbs for
wave start, fabrication start/end, adaptation applied, core breach. Errors and Unity logs flow automatically.

### S3 — Trace propagation
`ArmoryTrace` helper: start a transaction per fabrication, create spans around each await, inject the
`sentry-trace` and `baggage` headers into the `UnityWebRequest`s hitting the gateway. Tag every transaction with
`wave`, `element`, `budget`, `offline_fallback`, `cache_hit`.

### S4 — Use the data (this is what's judged)
- Read the traces, find the slowest hop, cut it (streaming transcription, parallel TTS/SFX) and screenshot the
  before/after trace for the demo.
- Keep the mic bug as the logs story: show the log line that now exists (`device=..., peak=0.00003, verdict=Silent`)
  next to the commit that fixed it.
- Show AI monitoring: cost/tokens per weapon, and the image-safety refusal rate that forced the prompt rewrite.

### S5 — Demo artefacts
A short "observability" slide/section in the README: the fabrication trace waterfall, the mic log, the profiling
flamegraph before/after pooling, and the uptime monitor.

## Acceptance
- One fabrication produces a single trace with 6+ spans, visible end to end.
- A forced silent mic produces a log with trace id and a user-visible message.
- Profiling shows the swarm-wave hitch and a measurable improvement after pooling.
- Uptime monitor green during the demo window.
