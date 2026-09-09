// wwwroot/janani-nav.js
// Plain <details>/<summary> dropdowns in NavMenu.razor need no JS to open/
// close themselves, but native <details> doesn't close a sibling when
// another opens, or close itself when a link inside is picked — this adds
// just those two behaviors.
document.addEventListener('toggle', (event) => {
    if (!(event.target instanceof HTMLElement) || !event.target.matches('.nav-dropdown')) return;
    if (event.target.open) {
        document.querySelectorAll('.nav-dropdown[open]').forEach((other) => {
            if (other !== event.target) other.removeAttribute('open');
        });
    }
}, true);

document.addEventListener('click', (event) => {
    if (!(event.target instanceof HTMLElement)) return;

    const link = event.target.closest('.nav-dropdown-menu a');
    if (link) {
        link.closest('.nav-dropdown')?.removeAttribute('open');
        return;
    }

    const summary = event.target.closest('.nav-dropdown-toggle');
    if (summary) {
        // Hover (below) already opened it -- without this, native <details>
        // would toggle it straight back closed on this same click, so
        // hovering then clicking would look like nothing happened.
        // Touch devices with no hover still open it via the native toggle,
        // since it isn't open yet in that case.
        if (summary.closest('.nav-dropdown')?.open) event.preventDefault();
        return;
    }

    // Click anywhere else outside a dropdown closes whatever's open, instead
    // of it staying open until something else happens to toggle it.
    if (event.target.closest('.nav-dropdown')) return;
    document.querySelectorAll('.nav-dropdown[open]').forEach((el) => el.removeAttribute('open'));
});

// Hovering opens a dropdown (delegated on document, not bound to specific
// elements, so it keeps working if Blazor re-renders NavMenu's DOM). It
// stays open once opened this way -- no mouseleave-close -- until the click
// handler above closes it, so moving the pointer down into the menu doesn't
// dismiss it.
document.addEventListener('mouseover', (event) => {
    const dropdown = event.target instanceof HTMLElement ? event.target.closest('.nav-dropdown') : null;
    if (dropdown && !dropdown.open) dropdown.open = true;
});
