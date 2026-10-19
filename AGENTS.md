# PoolCar agent guide

PoolCar is an internal shared-car application. Web (Razor Pages + htmx) is the only user entry point and is signed in through EasyAuth. It calls two internal APIs over HTTP: Fleet (a read-only car catalog loaded from `src/PoolCar.Fleet/Data/fleet.json`) and Reservations (bookings and employees in PostgreSQL). Aspire AppHost wires them together locally.

## Where to read more

| Before you | Read |
| --- | --- |
| Change pages | [docs/web-ui.md](docs/web-ui.md): htmx rules and the pattern to follow for each case |
| Change an API | [docs/api.md](docs/api.md): endpoints, headers and error codes |
| Change architecture, data storage, sign-in or hosting | The relevant record in [docs/adr/](docs/adr/README.md); add a record for a new decision with real tradeoffs |
| Run tests or sign in as a seeded employee | [docs/development.md](docs/development.md) |
| Change infrastructure or the Deploy workflow | [docs/deployment.md](docs/deployment.md) |

## Layout

- `src/PoolCar.Web/`: pages in `Pages/` with Dutch route names, view models and logic in feature folders (`Planning/`, `Desk/`, ...), API clients in `Clients/`, and htmx and formatting helpers in `Htmx/` and `Formatting/`.
- `src/PoolCar.Fleet/`, `src/PoolCar.Reservations/`: Minimal APIs split into `Domain/`, `Data/` and `Endpoints/`.
- `src/PoolCar.DbSetup/`: Azure-only job that creates, migrates and seeds a deployment's database.
- `src/PoolCar.AppHost/`, `src/PoolCar.ServiceDefaults/`: Aspire wiring and shared telemetry, resilience and health checks.
- `tests/`: one test project per service, plus `ArchitectureTests` and `EndToEnd.Tests`.

## Run and test

- In a fresh clone or worktree, run `dotnet tool restore && dotnet husky install`. Committing needs gitleaks, and the Bicep CLI when Bicep files are staged.
- Start the app with `scripts/run-local.sh`; it prints `PoolCar web UI: <url>` once the EasyAuth proxy is healthy. Aspire assigns every port, so never hard-code one. On the sign-in form, enter any username; enter `FleetOperator` in Roles for the operator pages.
- Tests use Microsoft.Testing.Platform: `dotnet test --project tests/PoolCar.Web.Tests --filter-method "*Name*"`. `dotnet test PoolCar.slnx` also runs the slow end-to-end tests. The Reservations, DbSetup and end-to-end tests need Docker.
- `dotnet husky run --group uncommitted` builds, checks formatting, lints Bicep and checks Markdown links for the uncommitted changes. The agent Stop hook runs it when you end a turn and returns failures for you to fix.
- Add a Reservations migration with `dotnet ef migrations add <Name> --project src/PoolCar.Reservations --output-dir Data/Migrations`.

## Conventions

- Write UI text and API error messages in Dutch, and code and documentation in English.
- Web never references the Fleet or Reservations projects; it uses the clients and contracts in `Clients/`. `ActingUserHandler` sends the signed-in employee to Reservations in the `X-PoolCar-Employee` and `X-PoolCar-Operator` headers.
- API clients only retry reads, because a retried write could book a car twice.
- Reservations domain methods return `Result`/`DomainError` instead of throwing. `Endpoints/ApiProblems.cs` maps them to problem details with a Dutch `detail` and a `code`, and Web reads them back as `ApiProblem`.
- Keep `Domain/` free of EF Core and ASP.NET Core. The architecture tests enforce this.
- Use `TimeProvider` (tests use `FakeTimeProvider`) and `TypedResults`. `BannedSymbols.txt` turns banned APIs into build errors (RS0030); add a line there when a convention bans an API.
- Bind service addresses to validated typed options and set them in AppHost.
- Forms must work without JavaScript; htmx only enhances them.
- Use relative links between Markdown files; the link check runs before every commit and in CI.

## Definition of done

- The build has no warnings, formatting passes, and the tests for the changed projects pass.
- Behavior changes have unit or API tests. Web page tests stub the APIs with `tests/PoolCar.Web.Tests/Support/StubHttpHandler.cs`.
- Each new user flow has an end-to-end test: walk through it in the running app with the Playwright MCP server (`.mcp.json`), then write the test following the existing ones in `tests/PoolCar.EndToEnd.Tests/`.
- Documentation that describes the changed behavior is updated.
