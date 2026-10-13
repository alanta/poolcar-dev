# PoolCar agent guide

PoolCar is an internal shared-car application. Web is the authenticated user entry point, Fleet owns fleet data, Reservations owns booking data, and Simulator is a client of both APIs. Aspire AppHost wires the local services and PostgreSQL.

## Layout

- `src/PoolCar.Web/` — Razor Pages UI, EasyAuth integration, and API clients.
- `src/PoolCar.Fleet/` — Fleet API.
- `src/PoolCar.Reservations/` — Reservations API.
- `src/PoolCar.Simulator/` — Simulator client.
- `src/PoolCar.AppHost/` — local Aspire resources and service wiring.
- `src/PoolCar.ServiceDefaults/` — shared telemetry, resilience, and health checks.
- `tests/PoolCar.*.Tests/` — unit, API, web, and end-to-end tests.
- `tests/PoolCar.ArchitectureTests/` — architecture rules.

## Run and test

- Start local services with `aspire run`; open the EasyAuth proxy at `http://localhost:5000` and sign in with a demo identity.
- Restore local tools with `dotnet tool restore`, then install hooks with `dotnet husky install`.
- Install gitleaks before committing; the hook fails explicitly when it is unavailable.
- Build with `dotnet build PoolCar.slnx`; run tests with `dotnet test PoolCar.slnx`.
- Add a Reservations migration with `dotnet ef migrations add <Name> --project src/PoolCar.Reservations --output-dir Data/Migrations`.
- End-to-end browser tests require Chromium. Install it with `pwsh tests/PoolCar.EndToEnd.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium`.
- In a fresh Git worktree, restore tools, install hooks, build, and install the Playwright browser before running browser tests. PostgreSQL data is isolated per AppHost worktree.

## Conventions

- Write Dutch UI text and English code.
- Use Minimal APIs with `TypedResults`.
- Keep domain code free of framework dependencies.
- Use `TimeProvider` for time.
- Bind service addresses to typed options, validate them at startup, and configure them in AppHost.
- Keep HTMX progressively enhanced: forms work without JavaScript, use the vendored script, and include the antiforgery request header for requests.
- Use the helpers in `src/PoolCar.Web/Htmx/` (`IsHtmx`, `PartialOrPage`, `HtmxRedirect`, `Toast`) and `src/PoolCar.Web/Formatting/` for dates, day parts, times and license plates.
- Run `dotnet format PoolCar.slnx --verify-no-changes` before completing a change.

## Definition of done

- Build without warnings and pass formatting checks.
- Add or update unit and API tests for behavior changes.
- Add an end-to-end test for each new user flow.
- Verify user-facing changes in a browser with Playwright MCP.
- Keep commits small and use clear messages.
