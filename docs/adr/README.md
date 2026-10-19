# Architecture decision records

Each record explains one decision with real tradeoffs: the context, the options and why we chose one. Read the relevant record before you change something it covers.

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](0001-split-the-backend-into-fleet-and-reservations-services.md) | Fleet and Reservations are separate services, with the web app as their first client. | Accepted |
| [0002](0002-implement-booking-rules-in-the-domain-model.md) | Booking rules live in a framework-free domain model; a database constraint guarantees no overlaps. | Accepted |
| [0003](0003-sign-in-with-platform-authentication.md) | Container Apps authentication signs users in; the services trust the acting-user headers. | Accepted |
| [0004](0004-render-the-ui-on-the-server-with-razor-pages-and-htmx.md) | Razor Pages with htmx, working without JavaScript. | Accepted |
| [0005](0005-apply-schema-changes-with-a-setup-job.md) | A setup job migrates the database; the running service has data access only. | Accepted |
| [0006](0006-guard-the-inner-loop-with-husky-and-agent-hooks.md) | Husky.Net defines the checks; Git and agent Stop hooks run them. | Accepted |

## Adding a record

Write an ADR for a decision with real tradeoffs, such as a new service, data store, hosting or security choice. Smaller choices, such as a library, don't need one.

1. Copy [template.md](template.md) to `NNNN-short-title.md` with the next number.
2. Fill it in, and add it to the table above.
3. When a decision changes, set the old record's status to "Superseded by NNNN" instead of rewriting it. Small follow-ups can be added to the record with an Updated date.
