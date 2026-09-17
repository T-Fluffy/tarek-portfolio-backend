# Portfolio.Backend — Contact Gateway (Render)

A minimal .NET 10 Web API that relays portfolio contact messages to **Resend**.
Live at `https://tarek-portfolio-backend.onrender.com` (Docker image built from this repo, auto-deployed on every `main` push).

## Endpoints

### `POST /api/contact/send`

Accepts a JSON contact message and relays it as an email. Rate-limited to **3 requests/min per connection IP** (excess gets `429` + `Retry-After: 60`). The bucket keys off the direct peer IP captured before header forwarding, so forged `X-Forwarded-For` values cannot mint fresh buckets; Turnstile (not the limiter) is the primary bot defense.

| Field            | Required | Rules                                                                 |
| :--------------- | :------- | :-------------------------------------------------------------------- |
| `name`           | Yes      | 1–100 chars, must not be blank                                         |
| `email`          | Yes      | Valid email address, max 254 chars                                     |
| `subject`        | Yes      | 1–200 chars, must not be blank (control chars are stripped)            |
| `message`        | Yes      | 1–5000 chars, must not be blank (HTML-encoded in the email body)       |
| `honeypot`       | No       | Hidden anti-bot field; any non-empty value returns fake `SUCCESS`      |
| `turnstileToken` | Sometimes | Required only when `TurnstileSecretKey` is configured (see below)     |

Request bodies are capped at 32 KB. The sender address is set as the email `reply_to` so replies go straight to the visitor.

### `GET /health`

Returns `{"status":"healthy"}`. Returns `503 {"status":"degraded","reason":"..."}` when the mail uplink (`ResendKey`) or the captcha gate (`TurnstileSecretKey`) is unconfigured — Render/docker health checks should treat non-200 as unhealthy.

## Environment variables (Render)

| Variable             | Required | Description                                                              |
| :------------------- | :------- | :----------------------------------------------------------------------- |
| `ResendKey`          | Yes      | Resend API key (`re_...`). Missing key is logged at startup (`CRITICAL`) |
| `TurnstileSecretKey` | In Production | Cloudflare Turnstile secret. Missing key logs `CRITICAL` at boot, fails `/health`, and fails sends with `503 captcha-unavailable`. Skipped in Development only |
| `PORT`               | No       | Port to bind (default `10000`); Render injects this automatically        |

CORS allows `https://t-fluffy.github.io` (plus `http://localhost:5173` / `http://localhost:3000` in Development only), methods `GET`/`POST`, header `Content-Type`, no credentials.

## Run locally

Windows (PowerShell):

```powershell
$env:ResendKey = "re_..."
dotnet restore
dotnet run
```

macOS / Linux:

```bash
export ResendKey=re_...
dotnet restore
dotnet run
```

The API listens on `http://0.0.0.0:10000` by default (override with `PORT`). A `Portfolio.Backend.http` file with sample `/health` and contact-send requests is included.

## Test

```bash
dotnet test
```

29 tests (xUnit.net v3 on Microsoft Testing Platform): model validation, controller coverage (honeypot silence, generic `502`, whitespace rejection, Turnstile branches, fail-closed `503`, payload sanitization/encoding), and full-pipeline integration tests (rate-limit `429` + spoof resistance, CORS allow/deny, honeypot-before-validation, health states). Note: the local machine needs the matching .NET runtime or `DOTNET_ROLL_FORWARD=LatestMajor` to execute the testhost.

## Security notes

- Input is validated (`[Required]`, `[EmailAddress]`, `[StringLength]`, blank rejection, control-char email reject); honeypot is bounded and checked before validation so bots always get fake `SUCCESS`.
- A missing `TurnstileSecretKey` fails closed in Production (`503 captcha-unavailable`); Development skips the check.
- Upstream Resend failures always surface as a generic `502` — provider status codes are logged server-side only, never forwarded.
- Security headers on all responses (`nosniff`, `DENY` framing, `no-referrer`, restrictive `Permissions-Policy`); unhandled errors become generic problem responses, never stack traces.
- Resend calls use a 10 s `HttpClient` timeout and honor request cancellation.
- The container runs as the non-root `app` user; base-image OS packages are patched at build time.
- Never commit `appsettings.json` / `appsettings.Development.json` (git-ignored); secrets come from environment variables only. (History was scrubbed of a once-committed credential before this repo went public.)

## CI & workflow

`main` is protected: all changes go through pull requests, and both status checks must pass — **Build, Test & Vulnerability Scan** (`dotnet` build + test + vulnerable-package scan) and **Docker image build + Trivy scan** (fails on HIGH/CRITICAL CVEs). Dependabot covers NuGet and Docker base images.
