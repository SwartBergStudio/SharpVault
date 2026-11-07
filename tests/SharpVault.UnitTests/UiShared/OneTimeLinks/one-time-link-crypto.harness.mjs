import { pathToFileURL } from "node:url";

import { readFileSync } from "node:fs";

const modulePath = process.argv[2];
const scenario = JSON.parse(readFileSync(0, "utf8"));
const crypto = await import(pathToFileURL(modulePath).href);
const results = [];

for (const step of scenario.steps) {
    if (step.op === "seal") {
        const sealed = await crypto.seal(new TextEncoder().encode(step.plaintext), step.passphrase ?? null);
        results.push({ payload: Buffer.from(sealed.payload).toString("base64"), fragment: sealed.fragment });
        continue;
    }

    if (step.op === "open") {
        try {
            const plaintext = await crypto.open(new Uint8Array(Buffer.from(step.payload, "base64")), step.fragment, step.passphrase ?? null);
            results.push({ plaintext });
        } catch (error) {
            results.push({ error: error.reason ?? error.name });
        }
        continue;
    }

    throw new Error("Unknown step " + step.op);
}

process.stdout.write(JSON.stringify(results));
