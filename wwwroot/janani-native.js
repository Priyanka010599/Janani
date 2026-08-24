// wwwroot/janani-native.js
// Google blocks OAuth sign-in when it detects the request coming from an
// embedded WebView (anti-phishing policy) — which is exactly what the
// Capacitor-wrapped mobile app's WebView looks like to Google. The fix is
// to open Google's sign-in specifically in the system browser (Chrome
// Custom Tabs / SFSafariViewController via @capacitor/browser), which
// Google does allow, instead of navigating the app's own WebView there.
// No-ops entirely outside the native app (window.Capacitor is only
// injected when running inside the Capacitor shell), so this never
// affects the regular website or the installed PWA.
document.addEventListener('click', (event) => {
    if (!window.Capacitor?.isNativePlatform?.()) return;

    const link = event.target.closest(
        'a[href*="/calendar/connect/google"], a[href*="/email-alerts/connect/google"]'
    );
    if (!link) return;

    event.preventDefault();
    window.Capacitor.Plugins.Browser.open({ url: link.href });
});
