# Task Manager API

A RESTful task management API built with **ASP.NET Core (.NET 10)**, **Entity Framework Core** and **SQLite**, secured with **JWT authentication**.

## Features
- User registration and login (BCrypt password hashing, JWT bearer tokens)
- Full CRUD for tasks, scoped per user (users can never see each other's data)
- Filtering by status, title search and pagination
- Input validation with proper HTTP status codes
- EF Core migrations (applied automatically on startup)
- Interactive API docs (Scalar / OpenAPI) in development
- Integration tests with xUnit and `WebApplicationFactory`

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
dotnet test
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
| DELETE | `/api/tasks/{id}` | Delete a task |

All `/api/tasks` routes require the header `Authorization: Bearer <token>`.

## Configuration
Set `Jwt:Key` (min. 32 characters) via environment variable `Jwt__Key` or user secrets in production. The development key in `appsettings.Development.json` is for local use only.
