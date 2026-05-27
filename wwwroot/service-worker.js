const CACHE_NAME = "success-tickets-pwa-v2";

// Archivos necesarios para instalar la PWA y usar el scanner QR.
const STATIC_ASSETS = [
    "/manifest.webmanifest",
    "/offline.html",
    "/favicon.ico",
    "/icons/icon.svg",
    "/icons/maskable-icon.svg",
    "/css/layout.css",
    "/css/site.css",
    "/js/site.js",
    "/lib/html5-qrcode/html5-qrcode.min.js"
];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => cache.addAll(STATIC_ASSETS))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys()
            .then((cacheNames) => Promise.all(
                cacheNames
                    .filter((cacheName) => cacheName !== CACHE_NAME)
                    .map((cacheName) => caches.delete(cacheName))
            ))
            .then(() => self.clients.claim())
    );
});

self.addEventListener("fetch", (event) => {
    const request = event.request;

    if (request.method !== "GET") {
        return;
    }

    const url = new URL(request.url);

    if (url.origin !== self.location.origin) {
        return;
    }

    if (request.mode === "navigate") {
        event.respondWith(
            fetch(request).catch(() => caches.match("/offline.html"))
        );
        return;
    }

    if (isStaticAsset(url.pathname)) {
        event.respondWith(cacheFirst(request));
    }
});

function isStaticAsset(pathname) {
    return pathname === "/manifest.webmanifest"
        || pathname === "/favicon.ico"
        || pathname.startsWith("/icons/")
        || pathname.startsWith("/css/")
        || pathname.startsWith("/js/")
        || pathname.startsWith("/lib/html5-qrcode/");
}

async function cacheFirst(request) {
    const cachedResponse = await caches.match(request);

    if (cachedResponse) {
        refreshCache(request);
        return cachedResponse;
    }

    const networkResponse = await fetch(request);
    const cache = await caches.open(CACHE_NAME);
    cache.put(request, networkResponse.clone());
    return networkResponse;
}

function refreshCache(request) {
    fetch(request)
        .then((response) => caches.open(CACHE_NAME)
            .then((cache) => cache.put(request, response)))
        .catch(() => {});
}
