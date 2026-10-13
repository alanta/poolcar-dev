htmx.config.responseHandling = [
    { code: "204", swap: false },
    { code: "[23]..", swap: true },
    { code: "409", swap: true, error: false },
    { code: "422", swap: true, error: false },
    { code: "[45]..", swap: false, error: true },
    { code: "...", swap: false },
];

document.addEventListener("click", (event) => {
    const button = event.target.closest("[data-toast-close]");
    button?.closest(".toast")?.remove();
});

document.addEventListener("animationend", (event) => {
    if (event.target.classList.contains("toast") && event.animationName === "toast-hide") {
        event.target.remove();
    }
});
