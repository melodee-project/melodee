// Cookie auth helpers for Blazor UI sign-in/sign-out flows.
window.melodeeAuth = (function () {
    async function postJson(url, payload) {
        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                credentials: 'same-origin',
                body: JSON.stringify(payload ?? {})
            });

            let message = null;
            let code = null;
            try {
                const body = await response.json();
                message = body?.message ?? body?.Message ?? body?.error ?? body?.Error ?? null;
                code = body?.code ?? body?.Code ?? null;
            } catch {
                // Ignore JSON parse errors for empty responses.
            }

            return {
                ok: response.ok,
                status: response.status,
                message: message,
                code: code
            };
        } catch (error) {
            return {
                ok: false,
                status: 0,
                message: error?.message ?? 'Request failed',
                code: null
            };
        }
    }

    return {
        signIn: function (userName, password) {
            return postJson('/api/v1/auth/cookie/sign-in', { userName: userName, password: password });
        },
        signInGoogle: function (idToken) {
            return postJson('/api/v1/auth/cookie/google', { idToken: idToken });
        },
        signOut: function () {
            return postJson('/api/v1/auth/cookie/sign-out', {});
        }
    };
})();
