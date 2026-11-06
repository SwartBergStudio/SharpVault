const activityEvents = ["pointerdown", "keydown", "wheel", "touchstart"];
const storageKey = "sharpvault.lastActivity";
const sessionKey = "sharpvault.activitySession";
const writeIntervalMs = 1000;
const heartbeatIntervalMs = 60000;
const retryAfterSubmitMs = 10000;
const clearGuardMs = 1000;
const pendingKey = Symbol.for("sharpvault.secretCopyPending");
const storedPendingKey = "sharpvault.secretCopyPending";

let timer = null;
let formId = null;
let timeoutMs = 0;
let lastActivity = 0;
let lastWrite = 0;
let lastBeat = 0;
let heartbeat = null;
let listening = false;

function readShared() {
    try {
        const value = Number(window.localStorage.getItem(storageKey));
        return Number.isFinite(value) && value > 0 ? value : 0;
    } catch {
        return 0;
    }
}

function writeShared(value) {
    try {
        window.localStorage.setItem(storageKey, String(value));
    } catch {
    }
}

function readSession() {
    try {
        return window.localStorage.getItem(sessionKey);
    } catch {
        return null;
    }
}

function writeSession(value) {
    try {
        window.localStorage.setItem(sessionKey, value);
    } catch {
    }
}

function latestActivity() {
    return Math.max(lastActivity, readShared());
}

function arm(delayMs) {
    clearTimeout(timer);
    timer = setTimeout(check, Math.max(delayMs, 0));
}

function beat(now) {
    if (heartbeat === null || now - lastBeat < heartbeatIntervalMs) {
        return;
    }
    lastBeat = now;
    try {
        heartbeat.invokeMethodAsync("Heartbeat").catch(() => {});
    } catch {
    }
}

function submitForm() {
    const form = document.getElementById(formId);
    if (form instanceof HTMLFormElement) {
        form.requestSubmit();
    }
}

function forgetStoredPending() {
    try {
        window.sessionStorage.removeItem(storedPendingKey);
    } catch {
    }
}

function signOut() {
    arm(retryAfterSubmitMs);
    if (globalThis[pendingKey] !== true) {
        submitForm();
        return;
    }
    let finished = false;
    let guard = null;
    const finish = () => {
        if (finished) {
            return;
        }
        finished = true;
        clearTimeout(guard);
        submitForm();
    };
    guard = setTimeout(finish, clearGuardMs);
    try {
        navigator.clipboard.writeText("").then(() => {
            globalThis[pendingKey] = false;
            forgetStoredPending();
            finish();
        }, finish);
    } catch {
        finish();
    }
}

function check() {
    if (formId === null) {
        return;
    }
    const remaining = timeoutMs - (Date.now() - latestActivity());
    if (remaining <= 0) {
        signOut();
    } else {
        arm(remaining);
    }
}

function recordActivity() {
    const now = Date.now();
    lastActivity = now;
    if (now - lastWrite >= writeIntervalMs) {
        lastWrite = now;
        writeShared(now);
    }
    arm(timeoutMs);
    beat(now);
}

function onStorage(event) {
    if (event.key === storageKey) {
        beat(Date.now());
        check();
    }
}

function onVisibility() {
    if (document.visibilityState === "visible") {
        check();
    }
}

function listen() {
    for (const name of activityEvents) {
        window.addEventListener(name, recordActivity, { passive: true });
    }
    window.addEventListener("storage", onStorage);
    window.addEventListener("pageshow", check);
    document.addEventListener("visibilitychange", onVisibility);
    listening = true;
}

function unlisten() {
    for (const name of activityEvents) {
        window.removeEventListener(name, recordActivity);
    }
    window.removeEventListener("storage", onStorage);
    window.removeEventListener("pageshow", check);
    document.removeEventListener("visibilitychange", onVisibility);
    listening = false;
}

export function start(signOutFormId, timeout, sessionTag, heartbeatRef) {
    stop();
    formId = signOutFormId;
    timeoutMs = timeout;
    heartbeat = heartbeatRef ?? null;
    const now = Date.now();
    const resumed = sessionTag && readSession() === sessionTag ? readShared() : 0;
    const fresh = resumed === 0;
    lastActivity = fresh ? now : resumed;
    lastWrite = now;
    if (fresh) {
        writeShared(now);
        writeSession(sessionTag);
    }
    listen();
    arm(fresh ? timeoutMs : timeout - (now - resumed));
}

export function stop() {
    clearTimeout(timer);
    timer = null;
    if (listening) {
        unlisten();
    }
    formId = null;
    heartbeat = null;
}
