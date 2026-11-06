import { pathToFileURL } from "node:url";

const [modulePath, scenarioJson] = process.argv.slice(2);
const scenario = JSON.parse(scenarioJson);

let reloads = 0;
let modalOpen = false;
const documentAttributes = new Set();
const listeners = new Map();
let now = 0;
const timers = new Map();
let nextTimerId = 1;

globalThis.setTimeout = (callback, delay) => {
    const id = nextTimerId++;
    timers.set(id, { callback, due: now + delay });
    return id;
};
globalThis.clearTimeout = (id) => {
    timers.delete(id);
};

function advance(milliseconds) {
    const target = now + milliseconds;
    for (;;) {
        const due = [...timers.entries()].filter(([, timer]) => timer.due <= target).sort((a, b) => a[1].due - b[1].due)[0];
        if (!due) {
            break;
        }
        timers.delete(due[0]);
        now = due[1].due;
        due[1].callback();
    }
    now = target;
}

class FakeInput {
    constructor(value) {
        this.value = value;
    }
}

class FakeTextarea extends FakeInput {}

class FakeSpan {
    constructor(text) {
        this.textContent = text;
    }
}

const elements = scenario.elements.map((element) => {
    switch (element.kind) {
        case "input":
            return new FakeInput(element.text);
        case "textarea":
            return new FakeTextarea(element.text);
        default:
            return new FakeSpan(element.text);
    }
});

const modal = {
    addEventListener: (name, handler) => listeners.set(name, handler),
    showModal: () => {
        modalOpen = true;
    },
    close: () => {
        modalOpen = false;
    },
    classList: { replace: () => {} },
};

const button = { addEventListener: () => {} };

globalThis.HTMLInputElement = FakeInput;
globalThis.HTMLTextAreaElement = FakeTextarea;
globalThis.location = {
    reload: () => {
        reloads += 1;
    },
};
globalThis.document = {
    visibilityState: "visible",
    documentElement: {
        setAttribute: (name) => documentAttributes.add(name),
        removeAttribute: (name) => documentAttributes.delete(name),
    },
    getElementById: (id) => (id === "components-reconnect-modal" ? modal : button),
    querySelectorAll: (selector) => (selector === "[data-sv-sensitive]" ? elements : []),
    addEventListener: () => {},
    removeEventListener: () => {},
};
globalThis.Blazor = {
    reconnect: async () => scenario.reconnect === "ok",
    resumeCircuit: async () => false,
};

await import(pathToFileURL(modulePath).href);

for (const state of scenario.states) {
    if (state.startsWith("advance:")) {
        advance(Number(state.slice(8)));
        continue;
    }
    listeners.get("components-reconnect-state-changed")({ detail: { state } });
}

process.stdout.write(
    JSON.stringify({
        reloads,
        modalOpen,
        hidden: documentAttributes.has("data-sv-disconnected"),
        texts: elements.map((element) => (element instanceof FakeInput ? element.value : element.textContent)),
    }),
);
