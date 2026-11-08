import { pathToFileURL } from "node:url";

const [modulePath, scenarioJson] = process.argv.slice(2);
const scenario = JSON.parse(scenarioJson);
const elements = new Map();
let clipboardValue = null;

Object.defineProperty(globalThis, "document", {
    configurable: true,
    value: { getElementById: (id) => elements.get(id) ?? null },
});
Object.defineProperty(globalThis, "navigator", {
    configurable: true,
    value: {
        clipboard: {
            writeText: async (value) => {
                clipboardValue = value;
            },
        },
    },
});

const create = await import(pathToFileURL(modulePath).href);
const passphraseInput = { value: scenario.passphrase ?? "" };
const output = { value: "" };
elements.set("pass", passphraseInput);
elements.set("out", output);

const sealed = await create.sealFromPage(scenario.snapshot, "pass");
const passphraseAfterSeal = passphraseInput.value;
const payloadLength = sealed.handle === undefined ? 0 : create.payload(sealed.handle).length;
create.publish(sealed.handle, scenario.baseUri, scenario.linkId, "out");
const copied = await create.copyLink("out");
const fragment = output.value.includes("#") ? output.value.slice(output.value.indexOf("#") + 1) : "";
create.discard(sealed.handle);

process.stdout.write(JSON.stringify({
    sealed: { status: sealed.status, hasHandle: sealed.handle !== undefined },
    payloadLength,
    fragment,
    copied,
    clipboard: clipboardValue,
    passphraseAfterSeal,
    passphraseCleared: passphraseInput.value === "",
    returned: [sealed, copied],
}));
