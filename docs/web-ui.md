# Web UI

Web renders pages on the server with Razor Pages and enhances them with htmx ([ADR 0004](adr/0004-render-the-ui-on-the-server-with-razor-pages-and-htmx.md)).

## Rules

- Every form works without JavaScript; htmx only enhances it.
- Use the vendored htmx in `wwwroot/lib/` and the helpers in `Htmx/` (`IsHtmx`, `PartialOrPage`, `HtmxRedirect`, `Toast`).
- Format dates, day parts, times and license plates with the helpers in `Formatting/`. Times are shown in Europe/Amsterdam.
- UI text is Dutch; routes are Dutch too, such as `/mijn-reserveringen`.
- Pages live in `Pages/`; their view models and logic live in feature folders such as `Planning/` and `Desk/`.

## Patterns

Follow the existing example of each pattern. Paths are relative to `src/PoolCar.Web/`.

| Pattern | Example |
| --- | --- |
| One handler returns a partial for htmx and the full page otherwise (`PartialOrPage`). | `Pages/Wagenpark.cshtml.cs` |
| Filter forms use `hx-get` with `hx-push-url="true"`, so the URL stays shareable. | `Pages/Planning.cshtml`, `Pages/Balie.cshtml` |
| Submit buttons use `hx-disabled-elt` and `hx-indicator` with a spinner. | `Pages/_ReservationCard.cshtml` |
| Toasts are appended out of band to `#toasts` (`ToastResult`), or stored in TempData before a redirect. | `Pages/Shared/_ToastOob.cshtml`, `Pages/MijnReserveringen.cshtml.cs` |
| `HtmxRedirect` sends `HX-Redirect` for htmx and a normal redirect otherwise. | `Pages/Nu.cshtml.cs` |
| Inline confirmation swaps the card for a confirm variant; without JavaScript the page renders that variant. | `OnGetCancelAsync` in `Pages/MijnReserveringen.cshtml.cs` |
| `409` and `422` responses are partials that `htmx.config.responseHandling` swaps in. | `wwwroot/js/site.js`, `Pages/Reserveren.cshtml.cs` |
| An unreachable API gives a `503` page that names the unavailable part, or a toast for a partial swap. | `Outages/ServiceUnavailableMiddleware.cs`, `wwwroot/js/site.js` |
| A failed antiforgery check is shown as an expired page: `ExpiredFormFilter` marks it, `/fout/400` shows a reload link or toast, and `site.js` swaps only 400s with `X-PoolCar-Expired`. | `ExpiredForms/ExpiredFormFilter.cs`, `Pages/Fout.cshtml` |
| Polling uses `hx-trigger="every 30s"` with `hx-sync`. | `Pages/_BalieBoard.cshtml` |
| `hx-boost` and the antiforgery `RequestVerificationToken` header are set on `<body>`. | `Pages/Shared/_Layout.cshtml` |
