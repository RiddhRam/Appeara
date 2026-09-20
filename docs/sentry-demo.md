# Sentry demo runbook

## Configure Unity

Create `UserSettings/ArmoryKeys.json` locally (it is ignored by Git):

```json
{
  "sentry": "https://PUBLIC_KEY@o0.ingest.sentry.io/PROJECT_ID",
  "sentryEnvironment": "demo"
}
```

The game initializes Sentry at startup with full trace and profile sampling for this demo build. It intentionally excludes audio, transcripts, prompts, provider keys, and generated images from events.

For the gateway mode, use the deployment variables in `Services/armory-gateway/.env.example`, set `ShipAI.Settings.GatewayUrl`, and optionally set its matching `GatewayToken`. Provider keys can then be removed from the Unity key file.

## Capture the judging evidence

1. Start a swarm wave, then fabricate one weapon by voice. In Sentry, open `armory.fabricate` and show the waterfall: `mic.capture`, `openai.transcribe`, `openai.weapon_spec`, `unity.assemble`, `elevenlabs.tts`, `elevenlabs.sfx`, and `openai.image` when enabled.
2. Force or reproduce a silent capture. Open the structured `Mic capture verdict` log and show device, peak, RMS, duration, verdict, and `armory.trace_id`; no speech content is present.
3. Open the `armory.wave` transaction/profile for the swarm. Compare its frame profile before and after pooling work.
4. Configure a Sentry Uptime monitor for the gateway's `GET /health` endpoint and show it green before the demo.

## Judge-ready explanation

> Sentry turned a multi-provider VR interaction into one observable player action. It exposed a real silent-microphone failure that Unity had been discarding without feedback, then showed exactly which AI or audio hop was delaying fabrication. We used those traces to focus latency work, while logs give us a privacy-safe diagnosis for every failed capture and profiling tells us whether swarm spectacle is hurting the frame budget.
