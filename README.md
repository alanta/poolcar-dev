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
| `POST /reservations` | Reserves a car (`carId`, `from`, `to`, `dayPart`) for at most 14 dates. |
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

Node.js LTS is needed to run the Playwright MCP server. PowerShell and Chromium are needed for browser tests.

## Run locally

Restore the repository tools, install the Git hooks, then start Aspire:

```sh
dotnet tool restore
dotnet husky install
scripts/run-local.sh
```

The script runs `aspire run --isolated`, so Aspire assigns every port and several worktrees can run side by side. It needs `jq`. Once the EasyAuth development proxy is healthy, the script prints `PoolCar web UI: <url>`; the proxy URL is also shown on the Aspire dashboard. Open that URL. On the sign-in form, enter a demo username such as `demo@example.test`; the user ID can be left at its generated value. Enter `FleetOperator` in Roles to try the operator view.

## Seeded employees

Locally, AppHost sets `Database:Migrate`, so the Reservations service migrates its database on startup and creates 30 fictional employees when it has none; AppHost also adds fixed sample bookings relative to today. In Azure the database setup job does this instead. To sign in as one of them, enter its ID in the User ID field of the sign-in form:

| Name | Office | ID |
| --- | --- | --- |
| Bram Testbeek | Utrecht | `f0671165-c417-421a-9eac-649e8534de6e` |
| Fleur Proefhuis | Amsterdam | `2c35f465-5839-4fc9-9bd6-1c56ee4f174f` |
| Daan Voorbeeld | Eindhoven | `fa91db2c-71ec-4752-beb9-4dcf1e2e58d4` |

Sample reservations are only created when `Seed:Reservations` is `true` and the database has no reservations. The database setup job always creates them in an empty database.

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

The pinned Playwright MCP server is configured in `.mcp.json`, which VS Code, Copilot CLI, the GitHub Copilot app and Claude Code all read. Start the app with `scripts/run-local.sh`, open the EasyAuth proxy URL, and sign in with a demo user before exploring the app.

## Manual setup

No manual setup is required to run the application locally. Deploying to Azure needs this one-time setup by an Azure and GitHub administrator:

- [ ] Register the resource providers once for the subscription:

  ```sh
  for namespace in Microsoft.App Microsoft.DBforPostgreSQL Microsoft.ContainerRegistry \
    Microsoft.OperationalInsights Microsoft.Insights Microsoft.ManagedIdentity; do
    az provider register --namespace "$namespace"
  done
  ```

- [ ] Create the resource group `rg-poolcar` in UK South (`uksouth`).
- [ ] Create the deploy identity:
  - [ ] Entra app registration `poolcar-github` with a federated credential for the subject `repo:alanta@73225/poolcar@1416094794:environment:azure`.
  - [ ] On `rg-poolcar`, assign it Contributor and Role Based Access Control Administrator, with the condition that it may only assign the AcrPull role.
- [ ] Create the sign-in app:
  - [ ] Entra app registration `PoolCar` for accounts in any organizational directory (multi-tenant), with ID tokens enabled: under Authentication, select "ID tokens" for implicit grant and hybrid flows, or run `az ad app update --id <appId> --enable-id-token-issuance true`. The built-in Container Apps authentication uses the hybrid flow.
  - [ ] App role `FleetOperator` with allowed member type Users/Groups, assigned to the fleet operators.
  - [ ] A client secret.
- [ ] Create the GitHub environment `azure` with:
  - [ ] Variables `AZURE_CLIENT_ID` (of `poolcar-github`), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and `EASYAUTH_CLIENT_ID` (of `PoolCar`). Optionally `AZURE_RESOURCE_GROUP`; it defaults to `rg-poolcar`.
  - [ ] Secret `EASYAUTH_CLIENT_SECRET` with the client secret of `PoolCar`.
- [ ] After the first deploy of each deployment, add its redirect URI `https://<web fqdn>/.auth/login/aad/callback` to `PoolCar` as a Web redirect URI. The workflow summary prints it; Entra ID doesn't allow wildcards.
- [ ] Protect `main`: require a pull request, require the status checks `Build`, `Test` and `End-to-end tests`, and block force pushes.
- [ ] Set the Actions log and artifact retention to 90 days.

## Deploying

The Deploy workflow deploys a named deployment to `rg-poolcar`. Pushes to `main` deploy `live`; run it manually for other deployments:

| Input | Description |
| --- | --- |
| `deployment` | Deployment name of 1-16 lowercase letters or digits, starting with a letter. Defaults to `live`. |
| `ref` | Branch, tag or commit to deploy. Defaults to `main`. |
| `reset-data` | Drop and reseed the deployment's database. |

A run deploys the shared infrastructure, builds the Web, Fleet, Reservations and DbSetup images and pushes them tagged with the commit SHA, runs the database setup job, and then deploys the apps. The job runs as `id-poolcar-dbadmin`, which owns the deployment's database: it creates the database, applies the Reservations migrations, seeds an empty database and gives the deployment's identity access to the data, but not to the schema. Reservations doesn't migrate or seed in Azure. The run summary shows the web URL and redirect URI. Runs are queued one at a time, also across deployments, because they share the PostgreSQL server.

The apps run one replica on weekdays from 9:00 to 17:00 Dutch time and scale to zero otherwise, so the first request outside those hours waits for a cold start.

To add a deployment, run the workflow with a new name, then add its redirect URI to the `PoolCar` app registration.

`reset-data` drops the deployment's database before setup, which creates, migrates and seeds it again with the employees and sample bookings, relative to the day they're seeded.

To remove a deployment, for example `scratch`, delete its resources and remove its redirect URI from `PoolCar`:

```sh
az containerapp delete -g rg-poolcar -n ca-scratch-web --yes
az containerapp delete -g rg-poolcar -n ca-scratch-reservations --yes
az containerapp delete -g rg-poolcar -n ca-scratch-fleet --yes
az containerapp job delete -g rg-poolcar -n caj-scratch-dbsetup --yes
az identity delete -g rg-poolcar -n id-poolcar-scratch
az postgres flexible-server db delete -g rg-poolcar -s psql-poolcar -n poolcar_scratch --yes
```

| Scope | Resources |
| --- | --- |
| Shared | Container registry `crpoolcar`, Container Apps environment `cae-poolcar`, PostgreSQL flexible server `psql-poolcar`, Log Analytics `log-poolcar`, Application Insights `appi-poolcar`, identities `id-poolcar-pull` (image pulls) and `id-poolcar-dbadmin` (database administrator). |
| Per deployment | Container apps `ca-<name>-web`, `ca-<name>-fleet` and `ca-<name>-reservations`, job `caj-<name>-dbsetup`, identity `id-poolcar-<name>` and database `poolcar_<name>`. |

Only Web is reachable from the internet; it signs users in with the `PoolCar` app registration.
