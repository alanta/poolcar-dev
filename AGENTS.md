# PoolCar agent guide

PoolCar is an internal shared-car application. Web (Razor Pages + htmx) is the only user entry point and is signed in through EasyAuth. It calls two internal APIs over HTTP: Fleet (a read-only car catalog loaded from `src/PoolCar.Fleet/Data/fleet.json`) and Reservations (bookings and employees in PostgreSQL). Aspire AppHost wires them together locally. The README documents the API endpoints, the error codes, the seeded employees and deployment.

## Layout

- `src/PoolCar.Web/`: pages in `Pages/` with Dutch route names, view models and logic in feature folders (`Planning/`, `Desk/`, ...), API clients in `Clients/`, and htmx and formatting helpers in `Htmx/` and `Formatting/`.
- `src/PoolCar.Fleet/`, `src/PoolCar.Reservations/`: Minimal APIs split into `Domain/`, `Data/` and `Endpoints/`.
- `src/PoolCar.DbSetup/`: Azure-only job that creates, migrates and seeds a deployment's database.
- `src/PoolCar.AppHost/`, `src/PoolCar.ServiceDefaults/`: Aspire wiring and shared telemetry, resilience and health checks.
- `tests/`: one test project per service, plus `ArchitectureTests` and `EndToEnd.Tests`.

## Run and test

- In a fresh clone or worktree, run `dotnet tool restore && dotnet husky install`. Committing needs gitleaks, and the Bicep CLI when Bicep files are staged.
- Start the app with `scripts/run-local.sh`; it prints `PoolCar web UI: <url>` once the EasyAuth proxy is healthy. Aspire assigns every port, so never hard-code one. Get the URL with `aspire describe easyauth --apphost src/PoolCar.AppHost/PoolCar.AppHost.csproj --format Json`.
- To sign in on the proxy's form, enter any username, or a seeded employee ID from the README as the user ID. Enter `FleetOperator` in Roles for the operator pages (`/balie`).
- Tests use Microsoft.Testing.Platform. Run one project with `dotnet test --project tests/PoolCar.Web.Tests` and filter with `--filter-method "*Name*"`. `dotnet test PoolCar.slnx` also runs the slow end-to-end tests.
- The Reservations and DbSetup tests need Docker (Testcontainers). The end-to-end tests start the whole AppHost with an empty database and need Chromium: `pwsh tests/PoolCar.EndToEnd.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium`.
- `dotnet husky run --group uncommitted` builds, checks formatting and lints Bicep for the uncommitted changes. The agent Stop hook (`.github/hooks/poolcar.json`, and `.claude/settings.json` for Claude Code) runs it when you end a turn and returns failures for you to fix.
- Add a Reservations migration with `dotnet ef migrations add <Name> --project src/PoolCar.Reservations --output-dir Data/Migrations`. Locally, Reservations migrates and seeds on startup (`Database:Migrate`); in Azure, DbSetup does that instead.

## Conventions

- Write UI text and API error messages in Dutch, and code in English.
- Web never references the Fleet or Reservations projects; it uses the clients and contracts in `Clients/`. `ActingUserHandler` sends the signed-in employee to Reservations in the `X-PoolCar-Employee` and `X-PoolCar-Operator` headers.
- API clients only retry reads, because a retried write could book a car twice.
- Reservations domain methods return `Result`/`DomainError` instead of throwing. `Endpoints/ApiProblems.cs` maps them to problem details with a Dutch `detail` and a `code`, and Web reads them back as `ApiProblem`.
- Keep `Domain/` free of EF Core and ASP.NET Core. The architecture tests enforce this.
- Use `TimeProvider` (tests use `FakeTimeProvider`) and `TypedResults`. `BannedSymbols.txt` turns banned APIs into build errors (RS0030); add a line there when a convention bans an API.
- Show dates and times in Europe/Amsterdam, using the helpers in `src/PoolCar.Web/Formatting/` for dates, day parts, times and license plates.
- Bind service addresses to validated typed options and set them in AppHost.
- Forms must work without JavaScript; htmx only enhances them. Use the vendored htmx in `wwwroot/lib/` and the helpers in `src/PoolCar.Web/Htmx/`.

## HTMX patterns

Follow the existing example of each pattern. Paths are relative to `src/PoolCar.Web/`.

| Pattern | Example |
| --- | --- |
| One handler returns a partial for htmx and the full page otherwise (`PartialOrPage`). | `Pages/Wagenpark.cshtml.cs` |
| Filter forms use `hx-get` with `hx-push-url="true"`, so the URL stays shareable. | `Pages/Planning.cshtml`, `Pages/Balie.cshtml` |
| Submit buttons use `hx-disabled-elt` and `hx-indicator` with a spinner. | `Pages/_ReservationCard.cshtml` |
| Toasts are appended out of band to `#toasts` (`ToastResult`), or stored in TempData before a redirect. | `Pages/Shared/_ToastOob.cshtml`, `Pages/MijnReserveringen.cshtml.cs` |
| `HtmxRedirect` sends `HX-Redirect` for htmx and a normal redirect otherwise. | `Pages/Nu.cshtml.cs` |
| Inline confirmation swaps the card for a confirm variant; without JavaScript the page renders that variant. | `OnGetCancelAsync` in `Pages/MijnReserveringen.cshtml.cs` |
| `409` and `422` responses are partials that `htmx.config.responseHandling` swaps in. | `wwwroot/js/site.js`, `Pages/Reserveren.cshtml.cs` |
| A failed antiforgery check is shown as an expired page: `ExpiredFormFilter` marks it, `/fout/400` shows a reload link or toast, and `site.js` swaps only 400s with `X-PoolCar-Expired`. | `ExpiredForms/ExpiredFormFilter.cs`, `Pages/Fout.cshtml` |
| Polling uses `hx-trigger="every 30s"` with `hx-sync`. | `Pages/_BalieBoard.cshtml` |
| `hx-boost` and the antiforgery `RequestVerificationToken` header are set on `<body>`. | `Pages/Shared/_Layout.cshtml` |

## Definition of done

- The build has no warnings, formatting passes, and the tests for the changed projects pass.
- Behavior changes have unit or API tests. Web page tests stub the APIs with `tests/PoolCar.Web.Tests/Support/StubHttpHandler.cs`.
- Each new user flow has an end-to-end test: walk through it in the running app with the Playwright MCP server (`.mcp.json`), then write the test following the existing ones in `tests/PoolCar.EndToEnd.Tests/`.
