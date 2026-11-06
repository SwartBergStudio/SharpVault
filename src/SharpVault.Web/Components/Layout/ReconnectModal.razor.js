const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);

const retryButton = document.getElementById("components-reconnect-button");
retryButton.addEventListener("click", retry);

const resumeButton = document.getElementById("components-resume-button");
resumeButton.addEventListener("click", resume);

const disconnectedAttribute = "data-sv-disconnected";
const sensitiveSelector = "[data-sv-sensitive]";
const wipeAfterMs = 30000;
let sensitiveWiped = false;
let wipeTimer = null;

function hideSensitiveValues() {
    document.documentElement.setAttribute(disconnectedAttribute, "");
}

function wipeSensitiveValues() {
    hideSensitiveValues();
    for (const element of document.querySelectorAll(sensitiveSelector)) {
        if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) {
            element.value = "";
        } else {
            element.textContent = "";
        }
        sensitiveWiped = true;
    }
}

function scheduleWipe() {
    clearTimeout(wipeTimer);
    wipeTimer = setTimeout(wipeSensitiveValues, wipeAfterMs);
}

function restoreSensitiveValues() {
    clearTimeout(wipeTimer);
    if (sensitiveWiped) {
        location.reload();
        return;
    }
    document.documentElement.removeAttribute(disconnectedAttribute);
}

function handleReconnectStateChanged(event) {
    const state = event.detail.state;
    if (state === "show" || state === "paused") {
        hideSensitiveValues();
        scheduleWipe();
    } else if (state === "failed" || state === "resume-failed" || state === "rejected") {
        wipeSensitiveValues();
    }

    if (state === "show") {
        reconnectModal.showModal();
    } else if (state === "hide") {
        reconnectModal.close();
        restoreSensitiveValues();
    } else if (state === "failed") {
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    } else if (state === "rejected") {
        location.reload();
    }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);

    try {
        const successful = await Blazor.reconnect();
        if (!successful) {
            const resumeSuccessful = await Blazor.resumeCircuit();
            if (!resumeSuccessful) {
                location.reload();
            } else {
                reconnectModal.close();
                restoreSensitiveValues();
            }
        }
    } catch (err) {
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    }
}

async function resume() {
    try {
        const successful = await Blazor.resumeCircuit();
        if (!successful) {
            location.reload();
        }
    } catch {
        reconnectModal.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        await retry();
    }
}
