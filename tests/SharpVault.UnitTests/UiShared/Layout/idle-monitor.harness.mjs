import { pathToFileURL } from "node:url";

const [modulePath, scenarioJson] = process.argv.slice(2);
const scenario = JSON.parse(scenarioJson);

let now = 1000000;
let nextTimerId = 1;
const timers = new Map();
const listeners = new Map();
const documentListeners = new Map();
const store = new Map();
const sessionStore = new Map();
let submits = 0;
const order = [];
const writes = [];
let heartbeats = 0;
const heartbeat = {
    invokeMethodAsync(name) {
        if (name === "Heartbeat") {
            heartbeats += 1;
        }
        return Promise.resolve();
    },
};

class FakeForm {
    requestSubmit() {
        submits += 1;
        order.push("submit");
    }
}

const form = new FakeForm();

function writeText(text) {
    writes.push(text);
    order.push("write:" + text);
    if (text !== "") {
        return Promise.resolve();
    }

    switch (scenario.clipboard ?? "ok") {
        case "reject":
            return Promise.reject(new Error("NotAllowedError"));
        case "throw":
            throw new Error("NotAllowedError");
        case "hang":
            return new Promise(() => {});
        default:
            return Promise.resolve();
    }
}

let clipboardRemoved = false;
Object.defineProperty(globalThis, "navigator", {
    value: {
        get clipboard() {
            return clipboardRemoved ? undefined : { writeText };
        },
    },
    configurable: true,
});

function add(map, name, handler) {
    const list = map.get(name) ?? [];
    list.push(handler);
    map.set(name, list);
}

function remove(map, name, handler) {
    map.set(name, (map.get(name) ?? []).filter((candidate) => candidate !== handler));
}

function dispatch(map, name, event) {
    for (const handler of [...(map.get(name) ?? [])]) {
        handler(event);
    }
}

globalThis.HTMLFormElement = FakeForm;
globalThis.Date = { now: () => now };
globalThis.setTimeout = (callback, delay) => {
    const id = nextTimerId++;
    timers.set(id, { callback, due: now + Math.max(delay, 0) });
    return id;
};
globalThis.clearTimeout = (id) => {
    timers.delete(id);
};
globalThis.window = {
    addEventListener: (name, handler) => add(listeners, name, handler),
    removeEventListener: (name, handler) => remove(listeners, name, handler),
    sessionStorage: {
        getItem: (key) => (sessionStore.has(key) ? sessionStore.get(key) : null),
        setItem: (key, value) => sessionStore.set(key, String(value)),
        removeItem: (key) => sessionStore.delete(key),
    },
    localStorage: {
        getItem: (key) => (store.has(key) ? store.get(key) : null),
        setItem: (key, value) => store.set(key, String(value)),
    },
};
globalThis.document = {
    visibilityState: "visible",
    getElementById: (id) => (id === scenario.formId ? form : null),
    addEventListener: (name, handler) => add(documentListeners, name, handler),
    removeEventListener: (name, handler) => remove(documentListeners, name, handler),
};

function advance(milliseconds) {
    const target = now + milliseconds;
    for (;;) {
        const due = [...timers.entries()]
            .filter(([, timer]) => timer.due <= target)
            .sort((left, right) => left[1].due - right[1].due)[0];
        if (!due) {
            break;
        }
        timers.delete(due[0]);
        now = Math.max(now, due[1].due);
        due[1].callback();
    }
    now = target;
}

const monitorUrl = pathToFileURL(modulePath).href;
const clipboardUrl = new URL("./clipboard.js", monitorUrl).href;
const monitor = await import(monitorUrl);
const copier = await import(clipboardUrl + "?instance=copier");
const clearer = await import(clipboardUrl + "?instance=clearer");

async function settle() {
    for (let index = 0; index < 20; index++) {
        await Promise.resolve();
    }
}

let navigations = 0;

for (const step of scenario.steps) {
    switch (step.op) {
        case "start":
            monitor.start(scenario.formId, step.timeoutMs, step.session ?? "", heartbeat);
            break;
        case "advance":
            advance(step.ms);
            break;
        case "activity":
            dispatch(listeners, step.event ?? "keydown", {});
            break;
        case "stop":
            monitor.stop();
            break;
        case "copy":
            await copier.writeText(step.text ?? "secret");
            clipboardRemoved = scenario.clipboard === "missing";
            break;
        case "clearClipboard":
            try {
                await clearer.clear();
            } catch {
            }
            break;
        case "navigate": {
            monitor.stop();
            globalThis[Symbol.for("sharpvault.secretCopyPending")] = undefined;
            const retry = globalThis[Symbol.for("sharpvault.secretCopyRetry")];
            if (retry) {
                globalThis.window.removeEventListener("focus", retry);
                document.removeEventListener("visibilitychange", retry);
                globalThis[Symbol.for("sharpvault.secretCopyRetry")] = undefined;
            }
            if (step.load) {
                await import(clipboardUrl + "?instance=page" + (++navigations));
            }
            break;
        }
        case "clipboardMode":
            scenario.clipboard = step.mode;
            break;
        case "focus":
            dispatch(listeners, "focus", {});
            break;
        case "visibility":
            document.visibilityState = step.state;
            dispatch(documentListeners, "visibilitychange", {});
            break;
        case "storage":
            store.set("sharpvault.lastActivity", String(now));
            dispatch(listeners, "storage", { key: "sharpvault.lastActivity" });
            break;
        default:
            throw new Error("Unknown step " + step.op);
    }
    await settle();
}

process.stdout.write(JSON.stringify({ submits, timers: timers.size, order, writes, heartbeats }));
