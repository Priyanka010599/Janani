// wwwroot/janani-push.js
// Real push notifications via Firebase Cloud Messaging — these arrive even
// when Janani isn't open, unlike janani-reminders.js's tab-open-only
// setTimeout-based browser notifications. The background half of this lives
// in service-worker.js's onBackgroundMessage handler.

const firebaseConfig = {
    apiKey: 'AIzaSyDnP6_2QB0PyKybCUv4GuwW0mkAA9WtIJk',
    authDomain: 'janani-505411.firebaseapp.com',
    projectId: 'janani-505411',
    storageBucket: 'janani-505411.firebasestorage.app',
    messagingSenderId: '52541450553',
    appId: '1:52541450553:web:11b3a75110c95b510e20e8',
};
const VAPID_KEY = 'BNlBYbhjhysoP5oRcR5KePMgI7v6MDC5bMBPrV2kxROKmHW5AoSNsZRxawBE8x3fVTLB3NgQjPNIUeq3qfU3pnI';

window.jananiPush = {
    isSupported: () => 'serviceWorker' in navigator && 'PushManager' in window && typeof firebase !== 'undefined',

    permission: () => ('Notification' in window ? Notification.permission : 'unsupported'),

    enable: async () => {
        if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
            return { ok: false, reason: 'unsupported' };
        }
        if (typeof firebase === 'undefined') {
            return { ok: false, reason: 'sdk-not-loaded' };
        }

        const permission = await Notification.requestPermission();
        if (permission !== 'granted') {
            return { ok: false, reason: 'denied' };
        }

        try {
            if (!firebase.apps.length) firebase.initializeApp(firebaseConfig);
            const messaging = firebase.messaging();
            const registration = await navigator.serviceWorker.ready;

            const token = await messaging.getToken({ vapidKey: VAPID_KEY, serviceWorkerRegistration: registration });
            if (!token) return { ok: false, reason: 'no-token' };

            const response = await fetch('/api/push/register', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ token }),
            });
            return { ok: response.ok, reason: response.ok ? null : 'server-error' };
        } catch {
            return { ok: false, reason: 'error' };
        }
    },
};
