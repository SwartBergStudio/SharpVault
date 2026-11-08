import { fragmentNeedsPassphrase, isSupported, LinkCryptoError, open } from "./one-time-link-crypto.js";

const OPEN_HEADER = "X-SharpVault-Link-Open";
const OPEN_HEADER_VALUE = "1";
const OPEN_SUFFIX = "/open";
const STATUS_NOT_FOUND = 404;
const STATUS_RATE_LIMITED = 429;
const FIELDS = ["name", "username", "url", "value", "notes"];
const REASON_PASSPHRASE_REQUIRED = "passphrase-required";
const REASON_AUTHENTICATION_FAILED = "authentication-failed";

const element = (id) => document.getElementById(id);
const sections = {
    start: element("otl-start"),
    notice: element("otl-passphrase-notice"),
    form: element("otl-passphrase-form"),
    passphraseError: element("otl-passphrase-error"),
    unavailable: element("otl-unavailable"),
    rateLimited: element("otl-rate-limited"),
    unsupported: element("otl-unsupported"),
    damaged: element("otl-damaged"),
    result: element("otl-result"),
};
const openButton = element("otl-open-button");
const passphraseInput = element("otl-passphrase-input");

let fragment = globalThis.location.hash.slice(1);
let pendingPayload = null;

function show(section) {
    section.hidden = false;
}

function hide(section) {
    section.hidden = true;
}

function showOnly(section) {
    for (const key of Object.keys(sections)) {
        if (sections[key] !== section && key !== "start" && key !== "notice") {
            hide(sections[key]);
        }
    }

    show(section);
}

function fail(section) {
    hide(sections.start);
    hide(sections.notice);
    showOnly(section);
}

function base64ToBytes(value) {
    const binary = atob(value);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) {
        bytes[index] = binary.charCodeAt(index);
    }

    return bytes;
}

async function requestPayload() {
    const path = globalThis.location.pathname.replace(/\/+$/, "") + OPEN_SUFFIX;
    return fetch(path, {
        method: "POST",
        headers: { [OPEN_HEADER]: OPEN_HEADER_VALUE },
        cache: "no-store",
        credentials: "omit",
    });
}

async function readPayload(response) {
    const body = await response.json();
    if (typeof body?.payload !== "string") {
        throw new Error("The payload is missing.");
    }

    return base64ToBytes(body.payload);
}

async function openLink() {
    openButton.disabled = true;
    let response;
    try {
        response = await requestPayload();
    } catch {
        fail(sections.unavailable);
        return;
    }

    if (response.status === STATUS_RATE_LIMITED) {
        show(sections.rateLimited);
        openButton.disabled = false;
        return;
    }

    if (response.status === STATUS_NOT_FOUND || !response.ok) {
        fail(sections.unavailable);
        return;
    }

    try {
        pendingPayload = await readPayload(response);
    } catch {
        fail(sections.damaged);
        return;
    }

    hide(sections.start);
    if (fragmentNeedsPassphrase(fragment)) {
        showPassphraseForm();
        return;
    }

    await tryOpen(null);
}

function showPassphraseForm() {
    hide(sections.passphraseError);
    show(sections.form);
    passphraseInput.focus();
}

async function tryOpen(passphrase) {
    let text;
    try {
        text = await open(pendingPayload, fragment, passphrase);
    } catch (error) {
        handleOpenFailure(error, passphrase);
        return;
    }

    render(text);
}

function handleOpenFailure(error, passphrase) {
    const reason = error instanceof LinkCryptoError ? error.reason : null;
    if (reason === REASON_PASSPHRASE_REQUIRED) {
        showPassphraseForm();
        return;
    }

    if (reason === REASON_AUTHENTICATION_FAILED && passphrase !== null) {
        show(sections.passphraseError);
        return;
    }

    pendingPayload = null;
    hide(sections.form);
    fail(sections.damaged);
}

function render(text) {
    let data;
    try {
        data = JSON.parse(text);
    } catch {
        pendingPayload = null;
        hide(sections.form);
        fail(sections.damaged);
        return;
    }

    for (const field of FIELDS) {
        const output = element(`otl-${field}`);
        const row = element(`otl-row-${field}`);
        const value = typeof data?.[field] === "string" ? data[field] : "";
        output.textContent = value;
        row.hidden = value === "";
    }

    pendingPayload = null;
    fragment = "";
    globalThis.history.replaceState(null, "", globalThis.location.pathname);
    hide(sections.start);
    hide(sections.notice);
    hide(sections.form);
    showOnly(sections.result);
}

async function submitPassphrase() {
    const passphrase = passphraseInput.value;
    passphraseInput.value = "";
    hide(sections.passphraseError);
    await tryOpen(passphrase);
}

async function copyField(field) {
    const text = element(`otl-${field}`).textContent;
    try {
        await navigator.clipboard.writeText(text);
    } catch {
    }
}

function wireCopyButtons() {
    for (const field of FIELDS) {
        element(`otl-copy-${field}`)?.addEventListener("click", () => copyField(field));
    }
}

function start() {
    if (!isSupported()) {
        fail(sections.unsupported);
        return;
    }

    if (fragment === "") {
        fail(sections.unavailable);
        return;
    }

    if (fragmentNeedsPassphrase(fragment)) {
        show(sections.notice);
    }

    openButton.addEventListener("click", openLink);
    element("otl-passphrase-submit").addEventListener("click", submitPassphrase);
    wireCopyButtons();
}

start();
