import "./clipboard.js";
import { start } from "./idle-monitor.js";

const config = document.querySelector("script[data-idle-form]");
if (config) {
    const timeout = Number(config.dataset.idleTimeoutMs);
    if (config.dataset.idleForm && Number.isFinite(timeout) && timeout > 0) {
        start(config.dataset.idleForm, timeout, config.dataset.idleSession ?? "");
    }
}
