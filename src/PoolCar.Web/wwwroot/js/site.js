htmx.config.responseHandling = [
    { code: "204", swap: false },
    { code: "[23]..", swap: true },
    { code: "409", swap: true, error: false },
    { code: "422", swap: true, error: false },
    { code: "[45]..", swap: false, error: true },
    { code: "...", swap: false },
];

// A backend service is unavailable, or the page expired because its form token is no longer valid: show the app's
// own error page, or only a toast for a partial swap. Other 503 responses, for example from the ingress during a
// restart, and other 400 responses are not swapped.
document.addEventListener("htmx:beforeSwap", (event) => {
    const xhr = event.detail.xhr;
    if ((xhr.status === 503 && xhr.getResponseHeader("X-PoolCar-Unavailable") === "true")
        || (xhr.status === 400 && xhr.getResponseHeader("X-PoolCar-Expired") === "true")) {
        event.detail.shouldSwap = true;
        event.detail.isError = false;
    }
});

document.addEventListener("click", (event) => {
    const closeButton = event.target.closest("[data-toast-close]");
    if (closeButton) {
        closeButton.closest(".toast")?.remove();
        return;
    }

    // Links to the search page focus the plate search field in the header instead
    const searchLink = event.target.closest("[data-focus-search]");
    const searchField = document.getElementById("kenteken-zoeken");
    if (searchLink && searchField && searchField.offsetParent !== null) {
        event.preventDefault();
        searchField.focus();
    }
});

document.addEventListener("animationend", (event) => {
    if (event.target.classList.contains("toast") && event.animationName === "toast-hide") {
        event.target.remove();
    }
});

// After a swap into a target with data-focus-heading, move focus to the new content's heading
document.addEventListener("htmx:afterSwap", (event) => {
    const target = event.detail.target;
    if (!target?.hasAttribute?.("data-focus-heading")) {
        return;
    }

    const heading = target.querySelector("h1, h2");
    if (heading) {
        heading.setAttribute("tabindex", "-1");
        heading.focus();
    }
});
