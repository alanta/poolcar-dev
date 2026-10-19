# API reference

Web is the only client of these APIs; neither is reachable from the internet. Why they are separate services: [ADR 0001](adr/0001-split-the-backend-into-fleet-and-reservations-services.md).

## Fleet API

Fleet management, such as adding or retiring cars, is out of scope for this version. The catalog of offices, categories and cars is maintained in `src/PoolCar.Fleet/Data/fleet.json`.

The Fleet service loads this file at startup and exposes it read-only:

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

The Reservations service stores employees and their bookings in PostgreSQL. Requests that act on behalf of an employee identify them with two headers ([ADR 0003](adr/0003-sign-in-with-platform-authentication.md)):

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
