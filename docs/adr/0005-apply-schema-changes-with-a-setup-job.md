# Apply schema changes with a setup job

## Status

Accepted

## Created / Updated

13-Oct-2026

## Deciders

Marnix van Valen

## Work Item

[#6](https://github.com/alanta/poolcar-base/pull/6), [#8](https://github.com/alanta/poolcar-base/pull/8)

## Context

Reservations stores its data in PostgreSQL and evolves the schema with EF Core migrations. In Azure it signs in to the database with its managed identity (Entra ID), so there are no database passwords. Each deployment needs its database created, migrated and seeded, also when its data is reset, and the running service should have no more rights than it needs.

## Decision drivers

* No passwords or connection-string secrets
* Least privilege for the running service
* The same repeatable setup for every deployment
* Simple local development

## Considered options

### Migrate at startup

The service applies pending migrations when it starts, so its identity owns the schema.

**Pro:**
* No separate step; the schema always matches the running code

**Con:**
* The running service can change or drop the schema
* Several replicas starting together race to migrate

### Migrate from the deploy workflow

The workflow runs `dotnet ef database update` or a migration bundle against the database.

**Pro:**
* No extra code in the repository

**Con:**
* The workflow needs network access to the database and administrator rights
* Database setup is split between the workflow and the app

### A setup job as database administrator

`PoolCar.DbSetup`, a console app that runs as a Container Apps job with an administrator identity. It creates the database and the `btree_gist` extension, applies the Reservations migrations, seeds an empty database, and grants the service's identity data access only (read, insert, update, delete, also on tables created later).

**Pro:**
* The running service can't change the schema
* One idempotent job for new deployments and for resets
* The database stays private to the Container Apps environment

**Con:**
* One more project, image and job to maintain
* The job must finish before the services are updated

## Decision

The decision has been made to apply schema changes with a setup job that runs as the database administrator, because it keeps the running service on least privilege and gives every deployment the same database setup.

### Consequences

* Reservations only migrates and seeds at startup when `Database:Migrate` is `true`, which AppHost sets for local runs.
* Reservations uses Entra authentication when its connection string has no password; locally it uses the password Aspire provides.
* The deploy workflow runs the job between building the images and deploying the services.

## Links

* [Deployment](../deployment.md#deploying)
* [Development: seeded employees and migrations](../development.md#seeded-employees)
