"""Private key-holding gateway for the Alien Armory fabrication loop.

Unity sends no OpenAI or ElevenLabs credential to this service.  The service also continues the
``sentry-trace``/``baggage`` headers Unity attaches, producing one cross-service trace per weapon.
"""

import hmac
import json
import logging
import os
from collections.abc import Mapping

import httpx
import sentry_sdk
from fastapi import FastAPI, Header, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import Response
from sentry_sdk.integrations.fastapi import FastApiIntegration
from sentry_sdk.ai.monitoring import record_token_usage

OPENAI_BASE = "https://api.openai.com/v1"
ELEVENLABS_BASE = "https://api.elevenlabs.io/v1"
ALLOWED_OPENAI = {"/chat/completions", "/audio/transcriptions", "/images/generations"}
ALLOWED_ELEVENLABS = {"/text-to-speech/", "/sound-generation"}

logger = logging.getLogger("armory.gateway")
logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"))

sentry_sdk.init(
    dsn=os.getenv("SENTRY_DSN"),
    environment=os.getenv("SENTRY_ENVIRONMENT", "demo"),
    release=os.getenv("RELEASE", "armory-gateway@dev"),
    traces_sample_rate=float(os.getenv("SENTRY_TRACES_SAMPLE_RATE", "1.0")),
    profiles_sample_rate=float(os.getenv("SENTRY_PROFILES_SAMPLE_RATE", "1.0")),
    enable_logs=True,
    integrations=[FastApiIntegration()],
    # Voice recordings and prompts must never be copied into telemetry.
    send_default_pii=False,
)

app = FastAPI(title="Alien Armory Gateway", version="1.0.0")
app.add_middleware(
    CORSMiddleware,
    allow_origins=[origin for origin in os.getenv("CORS_ORIGINS", "*").split(",") if origin],
    allow_methods=["POST", "GET"],
    allow_headers=["Content-Type", "sentry-trace", "baggage", "X-Armory-Gateway-Key"],
)


def require_gateway_key(value: str | None) -> None:
    expected = os.getenv("ARMORY_GATEWAY_TOKEN")
    if expected and not (value and hmac.compare_digest(value, expected)):
        raise HTTPException(status_code=401, detail="Invalid gateway token")


def tracing_headers(request: Request) -> dict[str, str]:
    """Forward only trace context; never forward Unity authorization headers to a provider."""
    return {
        name: request.headers[name]
        for name in ("sentry-trace", "baggage")
        if request.headers.get(name)
    }


async def proxy(request: Request, target: str, provider_headers: Mapping[str, str]) -> Response:
    body = await request.body()
    headers = {**tracing_headers(request), **provider_headers}
    content_type = request.headers.get("content-type")
    if content_type:
        headers["content-type"] = content_type

    # Do not log a request body: it can contain recorded player speech or a prompt.
    is_weapon_model_call = target.endswith("/chat/completions")
    with sentry_sdk.start_span(
        op="ai.chat_completions.create" if is_weapon_model_call else "http.client",
        name="OpenAI weapon spec" if is_weapon_model_call else request.url.path,
    ) as span:
        span.set_data("provider", "openai" if target.startswith(OPENAI_BASE) else "elevenlabs")
        async with httpx.AsyncClient(timeout=httpx.Timeout(65.0)) as client:
            upstream = await client.post(target, content=body, headers=headers)
        span.set_data("http.status_code", upstream.status_code)
        if is_weapon_model_call and upstream.is_success:
            try:
                usage = json.loads(upstream.content).get("usage", {})
                input_tokens = usage.get("prompt_tokens", usage.get("input_tokens", 0))
                output_tokens = usage.get("completion_tokens", usage.get("output_tokens", 0))
                record_token_usage(
                    span,
                    input_tokens=input_tokens,
                    output_tokens=output_tokens,
                    total_tokens=usage.get("total_tokens", input_tokens + output_tokens),
                )
                span.set_data("ai.usage.input_tokens", input_tokens)
                span.set_data("ai.usage.output_tokens", output_tokens)
                span.set_data("ai.model", json.loads(body).get("model", "unknown"))
            except (TypeError, ValueError, UnicodeDecodeError):
                # Monitoring must not turn a successful weapon fabrication into a failed one.
                span.set_data("ai.usage", "unavailable")
        logger.info("gateway.request endpoint=%s status=%s", request.url.path, upstream.status_code)

    passthrough = {"content-type": upstream.headers.get("content-type", "application/octet-stream")}
    return Response(content=upstream.content, status_code=upstream.status_code, headers=passthrough)


@app.get("/health")
async def health() -> dict[str, object]:
    """Use this URL for Sentry Uptime Monitoring; it intentionally reveals no credentials."""
    return {
        "ok": bool(os.getenv("OPENAI_API_KEY") and os.getenv("ELEVENLABS_API_KEY")),
        "service": "armory-gateway",
    }


@app.post("/openai/v1/{path:path}")
async def openai(path: str, request: Request, x_armory_gateway_key: str | None = Header(default=None)) -> Response:
    require_gateway_key(x_armory_gateway_key)
    suffix = "/" + path
    if suffix not in ALLOWED_OPENAI:
        raise HTTPException(status_code=404, detail="Unsupported OpenAI route")
    key = os.getenv("OPENAI_API_KEY")
    if not key:
        raise HTTPException(status_code=503, detail="OpenAI is not configured")
    return await proxy(request, OPENAI_BASE + suffix, {"Authorization": "Bearer " + key})


@app.post("/elevenlabs/v1/{path:path}")
async def elevenlabs(path: str, request: Request, x_armory_gateway_key: str | None = Header(default=None)) -> Response:
    require_gateway_key(x_armory_gateway_key)
    suffix = "/" + path
    if not any(suffix == prefix or suffix.startswith(prefix) for prefix in ALLOWED_ELEVENLABS):
        raise HTTPException(status_code=404, detail="Unsupported ElevenLabs route")
    key = os.getenv("ELEVENLABS_API_KEY")
    if not key:
        raise HTTPException(status_code=503, detail="ElevenLabs is not configured")
    return await proxy(request, ELEVENLABS_BASE + suffix, {"xi-api-key": key})
