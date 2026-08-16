# Portfolio.Backend — Contact Gateway (Render)

A minimal .NET 8 Web API that relays portfolio contact messages to **Resend**.

## Environment variables (Render)

| Variable    | Required | Description                                      |
| :---------- | :------- | :----------------------------------------------- |
| `ResendKey` | Yes      | Resend API key (`re_...`)                        |
| `PORT`      | No       | Port to bind; Render injects this automatically  |

CORS is pre-configured for `http://localhost:5173`, `http://localhost:3000` and
`https://T-Fluffy.github.io`.

## Run locally

```bash
export ResendKey=re_...
dotnet restore
dotnet run
```

## Test

```bash
dotnet test
```

## Security notes

- Contact input is validated (`[Required]`, `[EmailAddress]`, `[StringLength]`).
- A hidden honeypot field rejects bot submissions silently.
- Email subjects are stripped of control characters; the HTML body HTML-encodes user input.
- The 3/min rate limiter keys off the real client IP (`X-Forwarded-For`), so it works behind Render's proxy.
- The container runs as a non-root user with a `/health` endpoint for health checks.
- Never commit `appsettings.json` (git-ignored); secrets come from environment variables.
