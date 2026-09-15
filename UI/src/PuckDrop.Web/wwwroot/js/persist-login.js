// Keeps users signed in after they close the site.
//
// Blazor's OIDC support stores the signed-in user - including its refresh token - in oidc-client's
// default store, sessionStorage, which is per tab and cleared when the site closes, and it exposes
// no option to change that. This wraps Blazor's internal AuthenticationService.createUserManagerCore
// (the one place the oidc-client UserManager is built) to:
//   - persist the user in localStorage, so Blazor's startup silent sign-in can renew the session
//     with the stored refresh token when the site is reopened;
//   - revoke that refresh token before the logout redirect, so a copy that outlives the browser
//     session can't be reused. Best-effort: a failed revoke is logged and logout carries on.
//
// It relies on Blazor/oidc-client internals. If a Blazor upgrade removes the hook, this does
// nothing and the app falls back to Blazor's default behaviour - the E2E PersistentLoginTests and
// LogoutTests flag that. Must load after AuthenticationService.js and before blazor.webassembly.js.
(() => {
    const authenticationService = window.AuthenticationService;
    if (!authenticationService || typeof authenticationService.createUserManagerCore !== 'function') {
        return;
    }

    // A separate prefix from oidc-client's own "oidc." login-state entries, which also live in
    // localStorage and are swept by its stale-state cleanup.
    const prefix = 'puckdrop.oidc.';

    // oidc-client's store contract: every method returns a Promise.
    const userStore = {
        set(key, value) {
            localStorage.setItem(prefix + key, value);
            return Promise.resolve();
        },
        get(key) {
            return Promise.resolve(localStorage.getItem(prefix + key));
        },
        remove(key) {
            const value = localStorage.getItem(prefix + key);
            localStorage.removeItem(prefix + key);
            return Promise.resolve(value);
        },
        getAllKeys() {
            const keys = [];
            for (let i = 0; i < localStorage.length; i++) {
                const key = localStorage.key(i);
                if (key && key.startsWith(prefix)) {
                    keys.push(key.substring(prefix.length));
                }
            }
            return Promise.resolve(keys);
        }
    };

    const createUserManagerCore = authenticationService.createUserManagerCore.bind(authenticationService);

    authenticationService.createUserManagerCore = settings => {
        const userManager = createUserManagerCore({ ...settings, userStore });

        const signoutRedirect = userManager.signoutRedirect.bind(userManager);
        userManager.signoutRedirect = async args => {
            try {
                await userManager.revokeAccessToken();
            } catch (error) {
                console.warn('PuckDrop: refresh token revocation failed; continuing with logout.', error);
            }

            return signoutRedirect(args);
        };

        return userManager;
    };
})();
