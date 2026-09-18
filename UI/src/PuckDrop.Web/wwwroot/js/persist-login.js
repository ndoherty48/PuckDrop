// Keeps users signed in after they close the site.
//
// Blazor keeps the OIDC user, refresh token included, in sessionStorage, which is cleared when the
// site closes. This wraps Blazor's internal createUserManagerCore to store it in localStorage
// instead, and to revoke the refresh token (best-effort) before the logout redirect.
//
// Relies on Blazor internals: if the hook disappears this does nothing, and the E2E
// PersistentLoginTests and LogoutTests fail. Load it between AuthenticationService.js and
// blazor.webassembly.js.
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
