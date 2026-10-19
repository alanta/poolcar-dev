# Implement booking rules in the domain model

## Status

Accepted

## Created / Updated

13-Oct-2026

## Deciders

Marnix van Valen

## Work Item

[#3](https://github.com/alanta/poolcar-base/pull/3)

## Context

Booking a car follows a set of rules: a reservation covers 1 to 14 dates, each date becomes a period for a day part, all dates are booked or none, a car can't have overlapping periods, and periods move through pick-up, return and cancellation. The rules must be easy to read, test and change, and they must hold when many employees book at the same time.

## Decision drivers

* Rules that are easy to read, test and change
* No double bookings, also under concurrent requests
* Clear, Dutch error messages for the user
* Independence from frameworks

## Considered options

### Rules in the database

Constraints, triggers and stored procedures enforce every rule.

**Pro:**
* Every write is checked, whoever makes it
* Correct under concurrency

**Con:**
* Business logic in SQL is hard to read and to unit test
* Errors arrive as database exceptions, far from a message the user understands

### Rules in endpoint or service code

The API handlers check the rules before saving with EF Core.

**Pro:**
* Little ceremony

**Con:**
* Rules mix with HTTP and data access, and spread over handlers
* Two concurrent requests can both pass a check before either saves

### A domain model, backed by one database constraint

Plain C# types in `Domain/` (`Reservation`, `ReservationPeriod`, `DayPart`, `AvailabilityPolicy`) hold the rules and return a `Result` with a `DomainError` instead of throwing. The one rule that concurrency can break, no overlapping periods for a car, is also guaranteed by a PostgreSQL exclusion constraint.

**Pro:**
* Rules are unit tested without a database or web host
* Errors carry a code and a Dutch message that the API passes on
* The constraint closes the race that code alone can't

**Con:**
* The overlap rule exists twice, in code and in the database
* The constraint depends on PostgreSQL (`btree_gist`)

## Decision

The decision has been made to implement the booking rules in a framework-free domain model and back the overlap rule with an exclusion constraint, because the rules stay readable and testable while the database guarantees the one invariant that concurrent requests could break.

### Consequences

* `Domain/` doesn't depend on EF Core, Npgsql or ASP.NET Core; the architecture tests enforce it.
* `Endpoints/ApiProblems.cs` maps domain errors to problem details: `invalid` to 422, `conflict` to 409 and `not-allowed` to 403. A constraint violation is mapped to the same 409.
* All periods of a reservation are saved in one transaction.
* A concurrency test books the same slot 10 times in parallel and expects exactly one success.

## Links

* [API reference: errors](../api.md#reservations-api)
* [PostgreSQL exclusion constraints](https://www.postgresql.org/docs/current/ddl-constraints.html#DDL-CONSTRAINTS-EXCLUSION)
