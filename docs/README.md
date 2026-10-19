# PoolCar documentation

## Architecture

```
         browser
            │
  ┌─────────▼──────────┐   Azure: Container Apps authentication (Entra ID)
  │ EasyAuth           │   Local: EasyAuth development proxy
  └─────────┬──────────┘
            │ X-MS-CLIENT-PRINCIPAL
  ┌─────────▼──────────┐
  │ Web                │   Razor Pages + htmx, the only public app
  └────┬──────────┬────┘
       │ HTTP     │ HTTP + X-PoolCar-Employee / X-PoolCar-Operator
  ┌────▼─────┐ ┌──▼───────────┐   ┌────────────┐
  │ Fleet    │ │ Reservations │───│ PostgreSQL │
  │          │ │ + employees  │   └────────────┘
  └──────────┘ └──────────────┘
```

- **Web** turns the signed-in user into an employee, calls both APIs and combines their answers. It never references the service projects ([ADR 0001](adr/0001-split-the-backend-into-fleet-and-reservations-services.md), [ADR 0004](adr/0004-render-the-ui-on-the-server-with-razor-pages-and-htmx.md)).
- **Fleet** serves offices, categories and cars, read-only.
- **Reservations** owns employees, reservations and their periods, and the booking rules ([ADR 0002](adr/0002-implement-booking-rules-in-the-domain-model.md)).
- **Sign-in** is done by the platform, and Web passes the acting user on in headers ([ADR 0003](adr/0003-sign-in-with-platform-authentication.md)).
- **Azure:** the services run on Container Apps, and a setup job owns the database schema ([ADR 0005](adr/0005-apply-schema-changes-with-a-setup-job.md)).
- **Locally**, the Aspire AppHost (`src/PoolCar.AppHost`) runs the services, PostgreSQL and the sign-in proxy.

## Documents

| Document | Read it to |
| --- | --- |
| [development.md](development.md) | Run the app, sign in as a seeded employee, run tests and understand the checks and agent hooks. |
| [web-ui.md](web-ui.md) | Change pages: htmx rules and the patterns to follow. |
| [api.md](api.md) | Call or change the Fleet and Reservations APIs, including error codes. |
| [deployment.md](deployment.md) | Set up Azure and deploy, add or remove a deployment. |
| [adr/](adr/README.md) | Understand why the architecture is the way it is, or record a new decision. |
