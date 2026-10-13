// Retires the offline service worker that early gallery builds registered (PWA manifest, since
// removed). The Uno bootstrapper still emits a caching service-worker.js; CI replaces it with this
// file so any browser still holding the old registration picks up an update that clears the
// caches and unregisters itself. The app itself no longer registers a worker (enable_pwa = false).
self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(keys.map(key => caches.delete(key)));
        await self.registration.unregister();
    })());
});
