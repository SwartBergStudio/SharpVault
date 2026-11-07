const VERSION_PLAIN = 0x80;
const VERSION_PASSPHRASE = 0x81;
const KEY_BYTES = 32;
const IV_BYTES = 12;
const SALT_BYTES = 16;
const ITERATION_BYTES = 4;
const TAG_BYTES = 16;
const PASSPHRASE_ITERATIONS = 600000;
const MIN_ITERATIONS = 100000;
const MAX_ITERATIONS = 10000000;
const MIN_PAYLOAD_BYTES = 1 + IV_BYTES + 1 + TAG_BYTES;
const PLAIN_HEADER_BYTES = 1 + IV_BYTES;
const PASSPHRASE_HEADER_BYTES = 1 + SALT_BYTES + ITERATION_BYTES + IV_BYTES;
const PASSPHRASE_PREFIX = "p.";
const PLAIN_INFO = "sharpvault.otl.v1.plain";
const PASSPHRASE_INFO = "sharpvault.otl.v1.passphrase";
const BASE64URL_PATTERN = /^[A-Za-z0-9_-]*$/;
const encoder = new TextEncoder();

export class LinkCryptoError extends Error {
    constructor(reason) {
        super(reason);
        this.name = "LinkCryptoError";
        this.reason = reason;
    }
}

export function isSupported() {
    return Boolean(globalThis.crypto?.subtle);
}

export function fragmentNeedsPassphrase(fragment) {
    return typeof fragment === "string" && fragment.startsWith(PASSPHRASE_PREFIX);
}

export async function seal(plaintextUtf8, passphrase) {
    const subtle = requireSubtle();
    const linkKey = randomBytes(KEY_BYTES);
    try {
        if (passphrase === null) {
            return await sealPlain(subtle, linkKey, plaintextUtf8);
        }

        return await sealWithPassphrase(subtle, linkKey, plaintextUtf8, passphrase);
    } finally {
        linkKey.fill(0);
    }
}

export async function open(payload, fragment, passphrase) {
    const subtle = requireSubtle();
    const linkKey = decodeLinkKey(fragment);
    try {
        const version = readVersion(payload);
        if (version === VERSION_PLAIN) {
            return await openPlain(subtle, linkKey, payload);
        }

        if (version === VERSION_PASSPHRASE) {
            return await openWithPassphrase(subtle, linkKey, payload, passphrase);
        }

        throw new LinkCryptoError("unsupported-version");
    } finally {
        linkKey.fill(0);
    }
}

function requireSubtle() {
    if (!isSupported()) {
        throw new Error("Web Crypto is not available.");
    }

    return globalThis.crypto.subtle;
}

function randomBytes(length) {
    return globalThis.crypto.getRandomValues(new Uint8Array(length));
}

function concatBytes(...parts) {
    const total = parts.reduce((sum, part) => sum + part.length, 0);
    const result = new Uint8Array(total);
    let offset = 0;
    for (const part of parts) {
        result.set(part, offset);
        offset += part.length;
    }

    return result;
}

function writeUint32(value) {
    const bytes = new Uint8Array(ITERATION_BYTES);
    new DataView(bytes.buffer).setUint32(0, value, false);
    return bytes;
}

function readUint32(bytes, offset) {
    return new DataView(bytes.buffer, bytes.byteOffset + offset, ITERATION_BYTES).getUint32(0, false);
}

async function sealPlain(subtle, linkKey, plaintext) {
    const iv = randomBytes(IV_BYTES);
    const header = concatBytes(Uint8Array.of(VERSION_PLAIN), iv);
    const aesKey = await deriveAesKey(subtle, linkKey, new Uint8Array(0), PLAIN_INFO);
    const ciphertext = await encrypt(subtle, aesKey, iv, header, plaintext);
    return { payload: concatBytes(header, ciphertext), fragment: encodeBase64Url(linkKey) };
}

async function sealWithPassphrase(subtle, linkKey, plaintext, passphrase) {
    const salt = randomBytes(SALT_BYTES);
    const iv = randomBytes(IV_BYTES);
    const header = concatBytes(Uint8Array.of(VERSION_PASSPHRASE), salt, writeUint32(PASSPHRASE_ITERATIONS), iv);
    const aesKey = await derivePassphraseKey(subtle, linkKey, salt, passphrase, PASSPHRASE_ITERATIONS);
    const ciphertext = await encrypt(subtle, aesKey, iv, header, plaintext);
    return { payload: concatBytes(header, ciphertext), fragment: PASSPHRASE_PREFIX + encodeBase64Url(linkKey) };
}

async function openPlain(subtle, linkKey, payload) {
    if (payload.length < PLAIN_HEADER_BYTES + TAG_BYTES) {
        throw new LinkCryptoError("malformed");
    }

    const header = payload.subarray(0, PLAIN_HEADER_BYTES);
    const iv = header.subarray(1);
    const ciphertext = payload.subarray(PLAIN_HEADER_BYTES);
    const aesKey = await deriveAesKey(subtle, linkKey, new Uint8Array(0), PLAIN_INFO);
    return decrypt(subtle, aesKey, iv, header, ciphertext);
}

async function openWithPassphrase(subtle, linkKey, payload, passphrase) {
    if (passphrase === null || passphrase === undefined) {
        throw new LinkCryptoError("passphrase-required");
    }

    if (payload.length < PASSPHRASE_HEADER_BYTES + TAG_BYTES) {
        throw new LinkCryptoError("malformed");
    }

    const salt = payload.subarray(1, 1 + SALT_BYTES);
    const iterations = readUint32(payload, 1 + SALT_BYTES);
    if (iterations < MIN_ITERATIONS || iterations > MAX_ITERATIONS) {
        throw new LinkCryptoError("malformed");
    }

    const ivOffset = 1 + SALT_BYTES + ITERATION_BYTES;
    const header = payload.subarray(0, PASSPHRASE_HEADER_BYTES);
    const iv = payload.subarray(ivOffset, PASSPHRASE_HEADER_BYTES);
    const ciphertext = payload.subarray(PASSPHRASE_HEADER_BYTES);
    const aesKey = await derivePassphraseKey(subtle, linkKey, salt, passphrase, iterations);
    return decrypt(subtle, aesKey, iv, header, ciphertext);
}

async function derivePassphraseKey(subtle, linkKey, salt, passphrase, iterations) {
    const passphraseKey = await derivePassphraseBits(subtle, passphrase, salt, iterations);
    try {
        const ikm = concatBytes(linkKey, passphraseKey);
        try {
            return await deriveAesKey(subtle, ikm, salt, PASSPHRASE_INFO);
        } finally {
            ikm.fill(0);
        }
    } finally {
        passphraseKey.fill(0);
    }
}

async function derivePassphraseBits(subtle, passphrase, salt, iterations) {
    const passwordBytes = encoder.encode(passphrase.normalize("NFKC"));
    try {
        const baseKey = await subtle.importKey("raw", passwordBytes, "PBKDF2", false, ["deriveBits"]);
        const bits = await subtle.deriveBits(
            { name: "PBKDF2", hash: "SHA-256", salt, iterations },
            baseKey,
            KEY_BYTES * 8);
        return new Uint8Array(bits);
    } finally {
        passwordBytes.fill(0);
    }
}

async function deriveAesKey(subtle, ikm, salt, info) {
    const baseKey = await subtle.importKey("raw", ikm, "HKDF", false, ["deriveKey"]);
    return subtle.deriveKey(
        { name: "HKDF", hash: "SHA-256", salt, info: encoder.encode(info) },
        baseKey,
        { name: "AES-GCM", length: KEY_BYTES * 8 },
        false,
        ["encrypt", "decrypt"]);
}

async function encrypt(subtle, aesKey, iv, additionalData, plaintext) {
    const ciphertext = await subtle.encrypt(
        { name: "AES-GCM", iv, additionalData, tagLength: TAG_BYTES * 8 },
        aesKey,
        plaintext);
    return new Uint8Array(ciphertext);
}

async function decrypt(subtle, aesKey, iv, additionalData, ciphertext) {
    let plaintext;
    try {
        plaintext = await subtle.decrypt(
            { name: "AES-GCM", iv, additionalData, tagLength: TAG_BYTES * 8 },
            aesKey,
            ciphertext);
    } catch {
        throw new LinkCryptoError("authentication-failed");
    }

    return decodeUtf8(new Uint8Array(plaintext));
}

function decodeUtf8(bytes) {
    try {
        return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
    } catch {
        throw new LinkCryptoError("malformed");
    }
}

function readVersion(payload) {
    if (!(payload instanceof Uint8Array) || payload.length < MIN_PAYLOAD_BYTES) {
        throw new LinkCryptoError("malformed");
    }

    return payload[0];
}

function decodeLinkKey(fragment) {
    if (typeof fragment !== "string") {
        throw new LinkCryptoError("malformed");
    }

    const encoded = fragment.startsWith(PASSPHRASE_PREFIX) ? fragment.slice(PASSPHRASE_PREFIX.length) : fragment;
    const key = decodeBase64Url(encoded);
    if (key.length !== KEY_BYTES) {
        key.fill(0);
        throw new LinkCryptoError("malformed");
    }

    return key;
}

function encodeBase64Url(bytes) {
    let binary = "";
    for (const byte of bytes) {
        binary += String.fromCharCode(byte);
    }

    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function decodeBase64Url(value) {
    if (!BASE64URL_PATTERN.test(value) || value.length % 4 === 1) {
        throw new LinkCryptoError("malformed");
    }

    const padded = value.replace(/-/g, "+").replace(/_/g, "/").padEnd(value.length + ((4 - (value.length % 4)) % 4), "=");
    const binary = atob(padded);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) {
        bytes[index] = binary.charCodeAt(index);
    }

    return bytes;
}
