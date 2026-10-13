# PoolCar

PoolCar is an internal application for coordinating shared company cars. It provides a single web entry point backed by Fleet and Reservations services.

## Fleet data

The fleet is a fixed snapshot taken from RDW open data (datasets `m9d7-ebf2` and `8ys7-d773`), stored in `src/PoolCar.Fleet/Data/fleet.json`. The plates and vehicle details are real public data; the app never calls RDW. Bookings and employees are fictional.

The Fleet service loads this file into memory at startup and exposes it read-only:

| Endpoint | Description |
| --- | --- |
| `GET /offices` | All offices. |
| `GET /categories` | Car categories in display order. |
| `GET /cars?office=&category=` | Cars, optionally filtered, sorted by category and then range (longest first). |
| `GET /cars?plates=A,B,C` | Cars for a comma-separated list of up to 200 plates. |
| `GET /cars/search?q=` | Up to 10 cars whose plate contains `q`. |
| `GET /cars/{plate}` | A single car, or `404` when the plate is unknown. |

Unknown offices or categories in a filter return `400` with a problem details body.

## Prerequisites

- .NET 10 SDK (the repository pins SDK 10.0.401).
- Aspire CLI 13.5 or later.
- Docker, for the local PostgreSQL resource and the Reservations database tests.
- Gitleaks for the pre-commit secret scan. See the [gitleaks installation guide](https://github.com/gitleaks/gitleaks#installing).

Node.js LTS is needed to run the Playwright MCP server in VS Code. PowerShell and Chromium are needed for browser tests.

## Run locally

Restore the repository tools, install the Git hooks, then start Aspire:

```sh
dotnet tool restore
dotnet husky install
aspire run
```

Open the EasyAuth development proxy at <http://localhost:5000>. On the sign-in form, enter a demo username such as `demo@example.test`; the user ID can be left at its generated value. Enter `FleetOperator` in Roles to try the operator view. The signed-in page shows the user's name, object ID, roles, and the Fleet and Reservations health results.

The Simulator is visible in the Aspire dashboard but does not start by default. To start it, pass `Simulator:Enabled=true` to the AppHost:

```sh
aspire run -- --Simulator:Enabled=true
```

## Tests and formatting

```sh
dotnet build PoolCar.slnx --configuration Release
dotnet test PoolCar.slnx
dotnet format PoolCar.slnx --verify-no-changes
```

The end-to-end test project uses Playwright. After building, install its Chromium browser with:

```sh
pwsh tests/PoolCar.EndToEnd.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
```

## Git hooks

Install the local tools and hooks with `dotnet tool restore && dotnet husky install`. Before a commit, the hook checks formatting on staged C# files and scans staged changes with `gitleaks`. The commit is blocked if gitleaks is missing; install it using the link above.

## Dev container

Open the repository in its Dev Container to get the .NET SDK, Aspire CLI, Docker-in-Docker, and the supporting tools. The post-create script restores tools, installs the hooks and gitleaks, builds the solution, and installs the Playwright Chromium browser.

## Playwright MCP

The pinned Playwright MCP server is configured in `.vscode/mcp.json`. Start the app with `aspire run`, open the EasyAuth proxy URL, and sign in with a demo user before exploring the app.

## Manual setup

No manual setup is required to run the application locally.
