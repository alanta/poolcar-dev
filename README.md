# PoolCar

PoolCar is an internal application for coordinating shared company cars. Employees reserve a car for a morning, an afternoon or a full day, pick it up and return it; fleet operators see the whole desk. A single web app is backed by a Fleet and a Reservations service. Employees and bookings are sample data.

## Prerequisites

- .NET 10 SDK (the repository pins SDK 10.0.401).
- Aspire CLI 13.5 or later.
- Docker, for the local PostgreSQL resource and the database tests.
- Gitleaks for the pre-commit secret scan. See the [gitleaks installation guide](https://github.com/gitleaks/gitleaks#installing).
- The Bicep CLI for the pre-commit lint of Bicep files. `az bicep install` is enough; the lint also looks in `~/.azure/bin`.
- `jq`, for `scripts/run-local.sh` and the agent Stop hook.

Node.js LTS is needed to run the Playwright MCP server. PowerShell and Chromium are needed for browser tests. The dev container has all of these.

## Run locally

Restore the repository tools, install the Git hooks, then start Aspire:

```sh
dotnet tool restore
dotnet husky install
scripts/run-local.sh
```

Open the URL the script prints (`PoolCar web UI: <url>`) and sign in on the form with any username. Enter `FleetOperator` in Roles to try the operator view, or a [seeded employee's ID](docs/development.md#seeded-employees) as the user ID.

## Build and test

```sh
dotnet build PoolCar.slnx
dotnet test PoolCar.slnx
dotnet husky run --group uncommitted   # the build, format, Bicep and link checks for your changes
```

The Git hooks run the same checks before each commit. See [Development](docs/development.md#tests) for running single test projects, the end-to-end tests and the checks.

## Documentation

- [Architecture overview and documentation index](docs/README.md)
- [Development](docs/development.md): running, testing, checks, agent hooks, dev container
- [Web UI](docs/web-ui.md): htmx rules and patterns
- [API reference](docs/api.md)
- [Deployment](docs/deployment.md): Azure setup and the Deploy workflow
- [Architecture decision records](docs/adr/README.md)

## License

[MIT](LICENSE)
