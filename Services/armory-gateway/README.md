# Appeara gateway

The gateway keeps provider keys out of Unity and joins Unity's `sentry-trace` and `baggage` headers to the server-side model calls.

## Run locally

1. Create a virtual environment, install `requirements.txt`, and copy `.env.example` to `.env` (or use your deployment secret store).
2. Run `uvicorn app:app --env-file .env --host 0.0.0.0 --port 8787` from this directory.
3. Set `ShipAI.Settings.GatewayUrl` to the reachable address, for example `http://192.168.1.10:8787`; set `GatewayToken` if `ARMORY_GATEWAY_TOKEN` is configured.
4. Remove the OpenAI and ElevenLabs values from `UserSettings/ArmoryKeys.json` for the build. Add the Sentry DSN there only if Unity telemetry should use a separate Sentry project.

## Demo evidence

- Configure a Sentry Uptime monitor against `GET /health` before the judging window.
- Run one voiced fabrication and show the trace: `mic.capture` → `openai.transcribe` → gateway request → `openai.weapon_spec` → `unity.assemble` → audio/image spans.
- The trace and structured log attributes intentionally exclude raw audio, transcript, prompt, and credentials.
