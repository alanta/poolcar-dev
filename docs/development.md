# Development

How to run, test and change PoolCar locally. The [README](../README.md#prerequisites) lists the prerequisites.

## Run locally

`scripts/run-local.sh` runs `aspire run --isolated`, so Aspire assigns every port and several worktrees can run side by side, each with its own PostgreSQL data. Once the EasyAuth development proxy is healthy, the script prints `PoolCar web UI: <url>`; the Aspire dashboard shows the same URL. To read it from another shell:

```sh
aspire describe easyauth --apphost src/PoolCar.AppHost/PoolCar.AppHost.csproj --format Json
```

On the proxy's sign-in form, enter any username, such as `demo@example.test`, and leave the user ID at its generated value. Enter `FleetOperator` in Roles to see the operator pages. How sign-in works: [ADR 0003](adr/0003-sign-in-with-platform-authentication.md).

## Seeded employees

Locally, AppHost sets `Database:Migrate`, so the Reservations service migrates its database on startup and creates 30 fictional employees when it has none; AppHost also adds fixed sample bookings relative to today. In Azure the database setup job does this instead ([ADR 0005](adr/0005-apply-schema-changes-with-a-setup-job.md)). To sign in as one of them, enter its ID in the User ID field of the sign-in form:

| Name | Office | ID |
| --- | --- | --- |
| Bram Testbeek | Utrecht | `f0671165-c417-421a-9eac-649e8534de6e` |
| Fleur Proefhuis | Amsterdam | `2c35f465-5839-4fc9-9bd6-1c56ee4f174f` |
| Daan Voorbeeld | Eindhoven | `fa91db2c-71ec-4752-beb9-4dcf1e2e58d4` |

Sample reservations are only created when `Seed:Reservations` is `true` and the database has no reservations. The database setup job always creates them in an empty database.

To add a migration:

```sh
dotnet ef migrations add <Name> --project src/PoolCar.Reservations --output-dir Data/Migrations
```

## Tests

| Project | What it covers | Needs |
| --- | --- | --- |
| `PoolCar.Fleet.Tests`, `PoolCar.ServiceDefaults.Tests` | Unit and API tests. | |
| `PoolCar.Reservations.Tests`, `PoolCar.DbSetup.Tests` | Domain, API and database tests against PostgreSQL. | Docker (Testcontainers) |
| `PoolCar.Web.Tests` | Pages and clients, with the APIs stubbed by `Support/StubHttpHandler.cs`. | |
| `PoolCar.ArchitectureTests` | Domain code stays free of frameworks; Web doesn't reference the APIs. | |
| `PoolCar.EndToEnd.Tests` | User flows in Chromium against the whole AppHost, with an empty database. | Docker, Chromium |

The tests run on Microsoft.Testing.Platform:

```sh
dotnet test --project tests/PoolCar.Web.Tests              # one project
dotnet test --project tests/PoolCar.Web.Tests --filter-method "*Reserve*"
dotnet test PoolCar.slnx                                   # everything, including end-to-end
```

Install Chromium for the end-to-end tests after a build:

```sh
pwsh tests/PoolCar.EndToEnd.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
```

A failed end-to-end test saves a Playwright trace and prints its path; CI uploads the traces as an artifact.

## Checks

The same checks run on your machine and in CI, so a change that passes locally passes the Build job too. Why they are set up this way: [ADR 0006](adr/0006-guard-the-inner-loop-with-husky-and-agent-hooks.md).

| Check | Where it is defined |
| --- | --- |
| Warnings fail the build, including code style and nullable warnings. | `Directory.Build.props`, `.editorconfig` |
| Banned APIs fail the build (RS0030), such as `DateTime.Now` instead of `TimeProvider` and `Results` instead of `TypedResults`. | `BannedSymbols.txt` |
| Formatting: `dotnet format PoolCar.slnx --verify-no-changes`. | `.editorconfig` |
| Bicep lint: any diagnostic fails. | `scripts/lint-bicep.sh` |
| Relative links and heading anchors in Markdown files. | `scripts/check-links.cs` |
| Secret scan of staged changes. | `.husky/check-secrets.sh` (gitleaks) |

[Husky.Net](https://alirezanet.github.io/Husky.Net/) runs them in two groups, defined in `.husky/task-runner.json`:

- `pre-commit` runs on every commit, for the staged files. It blocks the commit when gitleaks is missing, or when Bicep files are staged and the Bicep CLI is missing.
- `uncommitted` runs the build, format, Bicep and link checks for everything changed since the last commit. Run it yourself with `dotnet husky run --group uncommitted`.

## Coding agents

`AGENTS.md` is the agent's entry point. Two pieces of tooling support agents:

- **Stop hook.** When an agent ends a turn with uncommitted changes, `scripts/agent-stop-hook.sh` runs the `uncommitted` group and sends any failure back to the agent to fix. `.github/hooks/poolcar.json` registers it for GitHub Copilot (CLI, app, VS Code and cloud agent); `.claude/settings.json` registers it for Claude Code. A working tree that was already checked is not checked again, so an agent that cannot fix a failure can still stop and report it.
- **Playwright MCP.** `.mcp.json` configures a pinned Playwright MCP server for walking through the app in a browser. Start the app and sign in first.

## Dev container

The dev container has the .NET SDK, the Aspire CLI, Docker-in-Docker and the other tools. Its post-create script restores the tools, installs the hooks, gitleaks and Bicep, builds the solution and installs Chromium.
