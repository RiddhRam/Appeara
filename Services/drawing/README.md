# Drawing recognition service

The workshop is drawing-first: draw → recognize → review alternatives → accept
or correct → equip the drawing as a paper weapon. No description is required.
The original transparent drawing is the artwork. This implementation does not
train Paper Cuts' models, use Redis, or regenerate/polish the image.

## Start locally

Python 3.9+ is sufficient; no packages are required. Configure environment variables
in your terminal (do not put API keys in Unity, tracked files, or a player build):

- `WEAPON_AI_PROVIDER`: `openai` or `gemini` (default `openai`).
- `WEAPON_AI_MODEL`: a model available to your account that supports image input
  and structured JSON output. Required; no model is silently substituted.
- `OPENAI_API_KEY` or `GEMINI_API_KEY`: the selected provider's secret.
- `WEAPON_AI_PORT`: optional, default `8787`.

Run from this folder:

```sh
python3 server.py
```

In Unity use Tools → Weapons → Open Playground, then Play. Expand Connection
settings and enter `http://127.0.0.1:8787/recognize`. Draw and click Bring my drawing
to life. The PNG is sent to the configured AI provider only at that point.
Without a configured server, the player chooses an interpretation manually.
The previous weapon remains usable if recognition fails or a proposal is rejected.

The service binds only to localhost and permits two concurrent model requests.
It is a development bridge, not a production deployment. For other devices, deploy
behind HTTPS with your own player authentication and rate/spend controls.
The client requires HTTPS except for loopback HTTP. No account/API calls were made
as part of implementation; provider adapters are covered by mocked contract tests.

## Contract

POST `/recognize`: `{"imageBase64":"<384×384 transparent PNG base64>"}`.
Returns `{"candidates":[{"label":"Knife","summary":"Swing your drawing as a blade.","recipe":{...}}]}`.
One to three candidates are required. Labels are at most 80 characters, summaries
300. Every recipe is also validated by Unity before it can be equipped.

The model selects an object label, one of seven behavior types, a status effect,
and bullet versus drawn ammunition. All recipes are version 2 and every weapon
repeats while held, limited by cooldown. Release stops repetition. There are no modifiers.
The seven types are projectile, beam,
explosion, spawn_object, apply_force, apply_status, and melee.
The server owns numeric damage, rate and travel limits. It compiles supported
combinations and describes their actual behavior; it never runs generated code.
Images and responses are not persisted by this bridge. Unity stores the accepted
recipe and original image together in `Application.persistentDataPath/drawn-weapon.json`.

OpenAI uses Responses API image input plus strict JSON Schema; Gemini uses
generateContent inline image data plus responseJsonSchema. Empty/refused/malformed
responses fail gracefully into the client's manual selection flow.

## Tests

```sh
python3 -m unittest -v test_server.py
```

In Unity Play mode: Tools → Weapons → Run Drawing Checks (Play Mode), and
Run Play Mode Combat Checks. No Unity batchmode is used.

Sources: [OpenAI vision](https://developers.openai.com/api/docs/guides/images-vision),
[structured output](https://developers.openai.com/api/docs/guides/structured-outputs),
[Gemini structured output](https://ai.google.dev/gemini-api/docs/generate-content/structured-output).
