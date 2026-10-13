htmx.config.responseHandling = [
    { code: "204", swap: false },
    { code: "[23]..", swap: true },
    { code: "409", swap: true, error: false },
    { code: "422", swap: true, error: false },
    { code: "[45]..", swap: false, error: true },
    { code: "...", swap: false },
];

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
