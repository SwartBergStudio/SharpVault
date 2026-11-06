const pendingKey = Symbol.for("sharpvault.secretCopyPending");
const retryKey = Symbol.for("sharpvault.secretCopyRetry");
const storedPendingKey = "sharpvault.secretCopyPending";
const page = globalThis.document;

function setPending(value) {
    globalThis[pendingKey] = value;
    try {
        if (value) {
            globalThis.window.sessionStorage.setItem(storedPendingKey, "1");
        } else {
            globalThis.window.sessionStorage.removeItem(storedPendingKey);
        }
    } catch {
    }
}

function readStoredPending() {
    try {
        return globalThis.window.sessionStorage.getItem(storedPendingKey) === "1";
    } catch {
        return false;
    }
}

function disarmRetry() {
    const retry = globalThis[retryKey];
    if (!retry) {
        return;
    }
    globalThis[retryKey] = undefined;
    globalThis.window.removeEventListener("focus", retry);
    page.removeEventListener("visibilitychange", retry);
}

function armRetry() {
    if (globalThis[retryKey]) {
        return;
    }
    let running = false;
    const retry = async () => {
        if (!globalThis[pendingKey]) {
            disarmRetry();
            return;
        }
        if (running || page.visibilityState === "hidden") {
            return;
        }
        running = true;
        try {
            await navigator.clipboard.writeText("");
            setPending(false);
            disarmRetry();
        } catch {
        } finally {
            running = false;
        }
    };
    globalThis[retryKey] = retry;
    globalThis.window.addEventListener("focus", retry);
    page.addEventListener("visibilitychange", retry);
}

export async function writeText(text) {
    await navigator.clipboard.writeText(text);
    setPending(text !== "");
}

export async function clear() {
    try {
        await navigator.clipboard.writeText("");
    } catch (error) {
        if (globalThis[pendingKey]) {
            armRetry();
        }
        throw error;
    }
    setPending(false);
    disarmRetry();
}

if (readStoredPending()) {
    globalThis[pendingKey] = true;
    armRetry();
    globalThis[retryKey]();
}
