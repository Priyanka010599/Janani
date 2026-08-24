// wwwroot/janani-reminders.js
// Browser Notification API wrapper for medicine/vitamin reminders.
// Notifications are scheduled with plain setTimeout in the browser tab —
// there is no server-side push, so they only fire while this tab stays
// open and the user has granted notification permission.

window.bloomReminders = (() => {
    let timers = [];

    return {
        isSupported: () => typeof window !== 'undefined' && 'Notification' in window,

        permission: () => ('Notification' in window ? Notification.permission : 'unsupported'),

        requestPermission: async () => {
            if (!('Notification' in window)) return 'unsupported';
            if (Notification.permission === 'granted') return 'granted';
            return await Notification.requestPermission();
        },

        clearAll: () => {
            timers.forEach(id => clearTimeout(id));
            timers = [];
        },

        schedule: (title, body, delayMs) => {
            if (!('Notification' in window) || Notification.permission !== 'granted') return;
            const id = setTimeout(() => {
                new Notification(title, { body: body, icon: '/favicon.png' });
            }, delayMs);
            timers.push(id);
        }
    };
})();
