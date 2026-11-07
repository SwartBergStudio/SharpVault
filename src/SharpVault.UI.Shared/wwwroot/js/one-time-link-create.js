import { isSupported, seal } from "./one-time-link-crypto.js";

const MIN_PASSPHRASE_LENGTH = 8;
const STATUS_SEALED = "sealed";
const STATUS_PASSPHRASE_TOO_SHORT = "passphrase-too-short";
const STATUS_UNSUPPORTED = "unsupported";
const sealedLinks = new Map();

export async function sealFromPage(snapshotJson, passphraseInputId) {
    if (!isSupported()) {
        return { status: STATUS_UNSUPPORTED };
    }

    const input = document.getElementById(passphraseInputId);
    const passphrase = (input?.value ?? "").normalize("NFKC");
    if (passphrase !== "" && Array.from(passphrase).length < MIN_PASSPHRASE_LENGTH) {
        return { status: STATUS_PASSPHRASE_TOO_SHORT };
    }

    const plaintext = new TextEncoder().encode(snapshotJson);
    try {
        const sealed = await seal(plaintext, passphrase === "" ? null : passphrase);
        const handle = globalThis.crypto.randomUUID();
        sealedLinks.set(handle, { ...sealed, passphraseInputId });
        return { status: STATUS_SEALED, handle };
    } finally {
        plaintext.fill(0);
    }
}

export function payload(handle) {
    return entryOf(handle).payload;
}

export function publish(handle, baseUri, linkId, outputInputId) {
    const { fragment, passphraseInputId } = entryOf(handle);
    sealedLinks.delete(handle);
    const output = document.getElementById(outputInputId);
    if (!output) {
        throw new Error("The link output is not on the page.");
    }

    output.value = `${baseUri}l/${linkId}#${fragment}`;
    const passphraseInput = document.getElementById(passphraseInputId);
    if (passphraseInput) {
        passphraseInput.value = "";
    }
}

export async function copyLink(outputInputId) {
    const value = document.getElementById(outputInputId)?.value ?? "";
    if (value === "") {
        return false;
    }

    try {
        await navigator.clipboard.writeText(value);
        return true;
    } catch {
        return false;
    }
}

export function discard(handle) {
    sealedLinks.delete(handle);
}

function entryOf(handle) {
    const entry = sealedLinks.get(handle);
    if (!entry) {
        throw new Error("The sealed link is no longer available.");
    }

    return entry;
}
