// Janani is a Blazor Server app — pages are rendered live over a SignalR
// connection, so there's no meaningful "offline mode" for app content.
// This worker only makes repeat loads faster (cache-first static assets)
// and shows a friendly offline page instead of the browser's default error
// when a navigation is attempted with no connection.
//
// It also carries Firebase Cloud Messaging's background handler (below) —
// merged into this same worker rather than a separate firebase-messaging-sw.js,
// since a page can only be controlled by one active service worker at the
// root scope. getToken() in janani-push.js is pointed at this registration.

// Bump this on any release where SHELL_ASSETS content meaningfully changes —
// it's the only thing that forces already-installed clients to drop a stale
// cached copy (e.g. of janani.css) and fetch fresh. A previous version of
// this worker cache-first'd janani.css under a name that never changed,
// which meant CSS edits silently never reached anyone who'd already loaded
// the app once — confirmed live on 2026-08-21. See the fetch handler below:
// shell assets are now network-first, not cache-first, so this class of bug
// can't recur even if a future edit forgets to bump this version.
const CACHE_NAME = 'janani-shell-v2';
const SHELL_ASSETS = [
    '/offline.html',
    '/janani.css',
    '/bootstrap/bootstrap.min.css',
    '/manifest.json',
    '/icons/icon-192.png',
    '/icons/icon-512.png',
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then((cache) => cache.addAll(SHELL_ASSETS))
    );
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(keys.filter((k) => k !== CACHE_NAME).map((k) => caches.delete(k)))
        )
    );
    self.clients.claim();
});

self.addEventListener('fetch', (event) => {
    if (event.request.mode === 'navigate') {
        event.respondWith(
            fetch(event.request).catch(() => caches.match('/offline.html'))
        );
        return;
    }

    if (SHELL_ASSETS.some((asset) => event.request.url.endsWith(asset))) {
        // Network-first: always try for a fresh copy so edits (especially to
        // janani.css) reach an already-installed client on the next load,
        // falling back to the cache only when actually offline.
        event.respondWith(
            fetch(event.request)
                .then((response) => {
                    const copy = response.clone();
                    caches.open(CACHE_NAME).then((cache) => cache.put(event.request, copy));
                    return response;
                })
                .catch(() => caches.match(event.request))
        );
    }
});

// ── Firebase Cloud Messaging — background handler ────────────────────────
// Only handles notifications that arrive while no Janani tab is focused;
// a foreground onMessage handler isn't needed since the whole point of a
// push alert is to reach the caregiver even when the tab isn't open.
importScripts('https://www.gstatic.com/firebasejs/10.13.0/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/10.13.0/firebase-messaging-compat.js');

firebase.initializeApp({
    apiKey: 'AIzaSyDnP6_2QB0PyKybCUv4GuwW0mkAA9WtIJk',
    authDomain: 'janani-505411.firebaseapp.com',
    projectId: 'janani-505411',
    storageBucket: 'janani-505411.firebasestorage.app',
    messagingSenderId: '52541450553',
    appId: '1:52541450553:web:11b3a75110c95b510e20e8',
});

const messaging = firebase.messaging();
messaging.onBackgroundMessage((payload) => {
    const title = payload.notification?.title ?? 'Janani';
    const body = payload.notification?.body ?? '';
    self.registration.showNotification(title, { body, icon: '/icons/icon-192.png' });
});
