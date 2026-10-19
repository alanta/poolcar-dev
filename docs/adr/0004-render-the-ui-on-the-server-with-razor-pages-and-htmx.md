# Render the UI on the server with Razor Pages and htmx

## Status

Accepted

## Created / Updated

13-Oct-2026

## Deciders

Marnix van Valen

## Work Item

[#4](https://github.com/alanta/poolcar-base/pull/4)

## Context

The web app is the first client of the Fleet and Reservations services ([ADR 0001](0001-split-the-backend-into-fleet-and-reservations-services.md)). Its UI is a set of forms and lists: reserve a car, see your reservations, the planning grid and the operator desk. It must work on a phone and feel responsive, and the team works in .NET.

## Decision drivers

* One language and toolchain
* Works on a phone and without JavaScript
* Little client-side state
* Easy to test

## Considered options

### Single-page app (React or similar)

**Pro:**
* Rich interactions

**Con:**
* A second toolchain (Node, bundler) and a second language
* State on both client and server, and sign-in handled in the browser

### Blazor

**Pro:**
* C# everywhere

**Con:**
* Server mode needs a live connection per user; WebAssembly mode is a large download
* Doesn't work without JavaScript

### Razor Pages with htmx

Pages are rendered on the server and work as plain HTML forms. htmx swaps parts of a page when JavaScript is available. htmx is vendored in `wwwroot/lib/`; there is no Node build.

**Pro:**
* One toolchain, and the pages work without JavaScript
* Page tests can assert on HTML with `WebApplicationFactory`

**Con:**
* Every handler serves both a full page and a partial
* Fewer ready-made components

## Decision

The decision has been made to use Razor Pages with htmx and plain CSS, because the UI is form-based and server rendering keeps it simple, accessible and in one toolchain.

### Consequences

* Handlers return a partial for htmx and the full page otherwise; shared helpers in `Htmx/` keep this consistent.
* Error responses (`409`, `422`, expired forms, unavailable services) need explicit handling in `site.js`.
* The look is plain CSS with design tokens and the self-hosted Inter font.

## Links

* [Web UI rules and patterns](../web-ui.md)
* [htmx](https://htmx.org/docs/)
