# Task Manager API

A RESTful task management API built with **ASP.NET Core (.NET 10)**, **Entity Framework Core** and **SQLite**, secured with **JWT authentication**.

## Features
- User registration and login (BCrypt password hashing, JWT bearer tokens)
- Full CRUD for tasks, scoped per user (users can never see each other's data)
- Soft delete with a recorded reason, a Deleted tab, restore and undo, plus permanent delete
- Filtering by status, title search and pagination
- Input validation with proper HTTP status codes
- EF Core migrations (applied automatically on startup)
- Interactive API docs (Scalar / OpenAPI) in development
- Login/registration rate limiting, token validation, startup checks of the configuration (see *Security notes*)
- 40 integration tests with xUnit and `WebApplicationFactory`, Dockerfile and GitHub Actions CI

## Frontend
A Blazor WebAssembly client (`src/TaskManager.Web`) with login/register, task list, filtering, search, pagination, status changes and dark mode support. The JWT is kept in `localStorage`.

## Tech stack
ASP.NET Core Web API · EF Core · SQLite · JWT · BCrypt · xUnit · Blazor WebAssembly · CORS

## Getting started
```bash
git clone <repo-url>
cd TaskManagerApi
dotnet run --project src/TaskManager.Api --launch-profile https   # terminal 1: API on https://localhost:7087
dotnet run --project src/TaskManager.Web --launch-profile https   # terminal 2: UI on https://localhost:7151
```
Open `https://localhost:<port>/scalar/v1` to explore the API.

Run the tests:
```bash
dotnet test        # 40 tests
```

Run the API in Docker (the Blazor client is a static site and is not part of the image):
```bash
docker build -t taskmanager-api .
docker run -p 8080:8080 -v taskmanager-data:/data \n  -e Jwt__Key='a-long-random-secret-of-at-least-32-characters' \n  -e Cors__AllowedOrigins__0=https://your-client.example taskmanager-api
```

## Endpoints
| Method | Route | Description |
|---|---|---|
| POST | `/api/auth/register` | Create an account, returns JWT |
| POST | `/api/auth/login` | Log in, returns JWT |
| GET | `/api/tasks?status=&search=&page=&pageSize=` | List tasks (paged, filterable) |
| GET | `/api/tasks/{id}` | Get a task |
| POST | `/api/tasks` | Create a task |
| PUT | `/api/tasks/{id}` | Update a task |
| GET | `/api/tasks/stats` | Counts per status, overdue and deleted |
| DELETE | `/api/tasks/{id}` | Soft delete (optional JSON body `{ "reason": "..." }`) |
| POST | `/api/tasks/{id}/restore` | Restore a soft-deleted task |
| DELETE | `/api/tasks/{id}/permanent` | Permanently remove a deleted task |

All `/api/tasks` routes require the header `Authorization: Bearer <token>`.

## Configuration
| Setting | Default | Meaning |
|---|---|---|
| `Jwt:Key` | none (required) | Token signing secret, **at least 32 characters**. Use user secrets or the `Jwt__Key` environment variable. The app **refuses to start** without a valid key, with a clear message. |
| `RateLimit:AuthPerMinute` | 10 | Login/register attempts per minute per client address |
| `Cors:AllowedOrigins` | the two local Blazor addresses | Browsers allowed to call the API |
| `HttpsRedirection` | true | Set to `false` behind a proxy that already terminates https |

The development key in `appsettings.Development.json` is for local use only.

## Security notes
- Passwords are hashed with BCrypt; passwords over 72 bytes (bcrypt's limit) are refused instead of silently truncated.
- Login and registration are rate limited (`429` with `Retry-After`). Login takes the same time for an unknown email and a wrong password.
- Tokens: signature, issuer, audience and lifetime are validated (30 s clock skew). A tampered, expired or foreign token is a `401`.
- Input is validated: lengths, e-mail format, and the task status must be a real value. Request bodies are capped at 64 KB.
- Errors are RFC 9457 problem documents; stack traces never reach the client in production.
- Known trade-offs: registering with an existing e-mail answers `409` (needed for a good sign-up experience, mitigated by the rate limit),
  and the Blazor demo keeps the JWT in `localStorage`, which a cross-site-scripting bug could read. A production app would use short-lived
  tokens with refresh tokens in `HttpOnly` cookies.
- Behind a reverse proxy, forward the client address (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`) or the rate limiter sees only the proxy.
