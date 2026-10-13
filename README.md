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

## Reservations API

The Reservations service stores employees and their bookings in PostgreSQL. Requests that act on behalf of an employee identify them with two headers:

- `X-PoolCar-Employee`: the employee's id (a GUID). Writes and `GET /reservations/mine` return `401` without a valid id.
- `X-PoolCar-Operator`: `true` for fleet operators. Employees may only change their own reservations; operators may change any reservation, otherwise the API returns `403`.

| Endpoint | Description |
| --- | --- |
| `GET /employees/{id}` | An employee, or `404`. |
| `PUT /employees/{id}` | Creates or updates an employee's name and home office. |
| `POST /reservations` | Reserves a car (`carId`, `from`, `to`, `dayPart`, optional `source`) for at most 14 dates. |
| `POST /reservations/now` | Reserves a car from now until the end of the current day part and picks it up. |
| `GET /reservations/mine` | The acting employee's reservations of the last 30 days and the future. |
| `GET /reservations/{id}` | A reservation with its periods, or `404`. |
| `POST /reservations/{id}/cancel` | Cancels the periods that have not been picked up. |
| `POST /periods/{id}/pickup` | Picks up the car of a running period. |
| `POST /periods/{id}/return` | Returns the car; an early return frees the rest of the period. |
| `POST /availability/busy` | The ids of the cars (`carIds`, at most 200) that are not free for a day part on every date from `from` to `to`, including cars that were not returned when the request includes today. |
| `POST /availability/now` | The ids of the cars (`carIds`, at most 200) that are in use now or were not returned. |
| `POST /schedule` | The periods that are not cancelled for the cars (`carIds`, at most 200) from `from` to `to`, with `notPickedUp`, `overdue` and `atRisk` flags. |

The availability and schedule queries do not change anything; they use `POST` to keep URLs short.

Errors are problem details with a Dutch `detail` and a machine-readable `code`:

| Status | Code | When |
| --- | --- | --- |
| `401` | `unauthorized` | The employee header is missing or invalid. |
| `403` | `not-allowed` | The reservation belongs to another employee. |
| `404` | `not-found` | The employee or reservation does not exist. |
| `409` | `conflict` | The car is already reserved (with `conflictingDates`), or the action does not fit the current state, such as cancelling a cancelled reservation or returning a car that was not picked up. |
| `422` | `invalid` | The request breaks a booking rule, such as more than 14 days, more than 200 cars or booking now outside day-part hours. |

In Development the OpenAPI document is available at `/openapi/v1.json`.

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

Open the EasyAuth development proxy at <http://localhost:5000>. On the sign-in form, enter a demo username such as `demo@example.test`; the user ID can be left at its generated value. Enter `FleetOperator` in Roles to try the operator view.

## Seeded employees

On startup the Reservations service creates 30 fictional employees when its database has none, and AppHost also adds fixed sample bookings relative to today. To sign in as one of them, enter its ID in the User ID field of the sign-in form:

| Name | Office | ID |
| --- | --- | --- |
| Bram Testbeek | Utrecht | `f0671165-c417-421a-9eac-649e8534de6e` |
| Fleur Proefhuis | Amsterdam | `2c35f465-5839-4fc9-9bd6-1c56ee4f174f` |
| Daan Voorbeeld | Eindhoven | `fa91db2c-71ec-4752-beb9-4dcf1e2e58d4` |

Sample reservations are only created when `Seed:Reservations` is `true` and the database has no reservations.

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
