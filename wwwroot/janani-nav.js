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
    const link = event.target instanceof HTMLElement ? event.target.closest('.nav-dropdown-menu a') : null;
    if (!link) return;
    link.closest('.nav-dropdown')?.removeAttribute('open');
});
