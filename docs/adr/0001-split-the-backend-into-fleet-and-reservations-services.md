# Split the backend into Fleet and Reservations services

## Status

Accepted

## Created / Updated

12-Oct-2026

## Deciders

Marnix van Valen

## Work Item

[#1](https://github.com/alanta/poolcar-base/pull/1), [#3](https://github.com/alanta/poolcar-base/pull/3)

## Context

PoolCar lets employees find and book shared company cars. The first client is a web app, but the same capabilities are wanted elsewhere: a mobile app, an MCP server so assistants can book a car, and integrations with facility and HR systems. The two halves of the domain also behave differently: the fleet is reference data that is read constantly and changes rarely, while reservations are transactional, write-heavy at the start of the day and need strict consistency.

## Decision drivers

* Reuse by other clients (mobile, MCP, integrations)
* Scale and deploy the parts independently
* Clear ownership of data and rules
* Operational complexity

## Considered options

### One web application

Pages, fleet data and booking rules in one ASP.NET Core app.

**Pro:**
* Simplest to build, test, deploy and operate
* No network calls or distributed failures

**Con:**
* Other clients need an API that is bolted onto the web app
* Everything scales together, and every change redeploys everything

### Modular monolith with an API

One deployable with separate modules for fleet and reservations, exposing an HTTP API next to the pages.

**Pro:**
* One deployable, with boundaries inside it
* Other clients can use the API

**Con:**
* Fleet and reservations still scale and fail together
* Module boundaries erode unless they are enforced

### Fleet and Reservations services behind a web front end

Fleet owns offices, categories and cars. Reservations owns employees, reservations and their periods, in its own database. Each is an HTTP API. The web app is one client: it combines both APIs for its pages and is the only part exposed to users.

**Pro:**
* New clients use the same APIs and rules as the web app
* Read-heavy fleet queries and transactional bookings scale separately
* Each service is small and owns its data

**Con:**
* Network calls between the parts, and failures to handle when a service is down
* More to deploy, monitor and secure, including how services know who acts (see [ADR 0003](0003-sign-in-with-platform-authentication.md))

## Decision

The decision has been made to split the backend into Fleet and Reservations services with the web app as their first client, because the APIs must serve more clients than the web app, and the two domains have different load and consistency needs.

### Consequences

* The web app composes the APIs for its pages, for example available cars are Fleet's cars minus the ones Reservations reports as busy.
* The web app never references the service projects; the architecture tests enforce it.
* The web app must handle an unavailable service: it shows which part is down instead of failing the whole page.
* Aspire runs the services together locally, and each service has its own health checks and telemetry name.

## Links

* [Architecture overview](../README.md)
* [API reference](../api.md)
