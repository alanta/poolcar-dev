# Sign in with platform authentication

## Status

Accepted

## Created / Updated

12-Oct-2026

## Deciders

Marnix van Valen

## Work Item

[#1](https://github.com/alanta/poolcar-base/pull/1), [#3](https://github.com/alanta/poolcar-base/pull/3)

## Context

Employees sign in with their Microsoft Entra ID work account, and fleet operators get extra pages. The services behind the web app need to know who acts, so employees can only change their own bookings. Developers and automated tests must be able to sign in as any employee or role locally, without a real tenant.

## Decision drivers

* Security, with as little authentication code in the app as possible
* The same sign-in flow locally and in Azure
* Tests that sign in as any employee or role
* Low effort to run

## Considered options

### ASP.NET Core Identity

Accounts and passwords stored by the app.

**Pro:**
* Works anywhere, no dependency on Entra ID

**Con:**
* Another set of credentials for employees, and password storage to secure
* No single sign-on with their work account

### OpenID Connect in the app

Microsoft.Identity.Web signs users in with Entra ID; the services validate access tokens.

**Pro:**
* Standard and portable to any host
* Tokens can be passed to the services

**Con:**
* Secrets, token handling and configuration in every app
* Local sign-in needs a tenant or a mock identity provider

### Platform authentication (Container Apps EasyAuth)

Container Apps signs users in with Entra ID and passes them to the web app in the `X-MS-CLIENT-PRINCIPAL` header. Locally, the EasyAuth development proxy (`Alanta.Aspire.Hosting.EasyAuthProxy`) does the same with a form where you enter any name, ID and roles.

**Pro:**
* Sign-in is configuration; the app only reads one header
* Local sign-in as anyone, which the end-to-end tests use
* No secrets in the app

**Con:**
* Tied to the hosting platform
* The app must only be reachable through the platform or the proxy

## Decision

The decision has been made to use platform authentication with an Entra ID app registration, because it keeps sign-in code and secrets out of the app and gives the same flow locally through the proxy.

A small authentication handler turns the header into a user: the Entra object ID is the employee ID, and the `FleetOperator` app role makes a user an operator. The web app passes the acting user to Reservations in the `X-PoolCar-Employee` and `X-PoolCar-Operator` headers, and Reservations checks ownership with them.

### Consequences

* **Security model:** the services have internal ingress only and trust the acting-user headers from anything inside the environment. That is acceptable for an internal app; a client outside the environment, such as a mobile app, needs its own authentication in front of the services.
* A signed-in user without an employee record picks a home office once, which creates the record.
* Operator rights come from an app role assignment in Entra ID.

## Links

* [API reference: acting-user headers](../api.md#reservations-api)
* [Deployment: the sign-in app](../deployment.md#manual-setup)
* [Container Apps authentication](https://learn.microsoft.com/azure/container-apps/authentication)
