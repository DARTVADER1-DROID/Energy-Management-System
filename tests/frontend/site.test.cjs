const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const script = fs.readFileSync(path.join(__dirname, "../../src/EnergyManagement.Api/wwwroot/js/site.js"), "utf8");

function boot({ storedTheme = null, prefersDark = false, storageReadThrows = false, storageWriteThrows = false } = {}) {
    const classes = new Set();
    const root = {
        dataset: {},
        classList: {
            toggle(name, enabled) { if (enabled) classes.add(name); else classes.delete(name); },
            add(name) { classes.add(name); },
            remove(name) { classes.delete(name); }
        }
    };
    const handlers = {};
    const themeToggle = {
        addEventListener(name, callback) { handlers[name] = callback; }
    };
    const mediaHandlers = {};
    const media = {
        matches: prefersDark,
        addEventListener(name, callback) { mediaHandlers[name] = callback; }
    };
    const localStorage = {
        getItem() {
            if (storageReadThrows) throw new Error("Storage blocked");
            return storedTheme;
        },
        setItem(_key, value) {
            if (storageWriteThrows) throw new Error("Storage blocked");
            storedTheme = value;
        }
    };
    const window = {
        localStorage,
        matchMedia: () => media
    };
    const document = {
        documentElement: root,
        getElementById(id) { return id === "theme-toggle" ? themeToggle : null; },
        querySelector() { return null; },
        querySelectorAll() { return []; },
        addEventListener() {}
    };
    vm.runInNewContext(script, { window, document, console });
    return { root, classes, handlers, mediaHandlers, getStoredTheme: () => storedTheme };
}

test("system preference initializes dark mode and applies the dark class", () => {
    const app = boot({ prefersDark: true });
    assert.equal(app.root.dataset.theme, "dark");
    assert.equal(app.classes.has("dark"), true);
});

test("invalid persisted theme falls back to system preference", () => {
    const app = boot({ storedTheme: "ultraviolet", prefersDark: false });
    assert.equal(app.root.dataset.theme, "light");
    assert.equal(app.getStoredTheme(), "system");
});

test("blocked local storage does not prevent application initialization", () => {
    const app = boot({ storageReadThrows: true, storageWriteThrows: true, prefersDark: true });
    assert.equal(app.root.dataset.theme, "dark");
    assert.doesNotThrow(() => app.handlers.click());
    assert.equal(app.root.dataset.theme, "light");
});

test("theme toggle updates the root class and remains usable when persistence fails", () => {
    const app = boot({ storedTheme: "light", storageWriteThrows: true });
    assert.equal(app.root.dataset.theme, "light");
    assert.doesNotThrow(() => app.handlers.click());
    assert.equal(app.root.dataset.theme, "dark");
    assert.equal(app.classes.has("dark"), true);
});

test("frontend script safely initializes when optional wizard and navigation elements are absent", () => {
    assert.doesNotThrow(() => boot());
});

test("purple brand tokens and readable muted text colours are defined for both themes", () => {
    const css = fs.readFileSync(path.join(__dirname, "../../src/EnergyManagement.Api/Web/Styles/tailwind.css"), "utf8");
    assert.match(css, /--color-blue-600:\s*#7c3aed/i);
    assert.match(css, /status-muted[\s\S]*text-slate-600[\s\S]*dark:text-slate-300/);
    assert.match(css, /html:not\(\.dark\)[\s\S]*\.text-slate-500\s*\{\s*color:\s*#475569/i);
    assert.match(css, /html\.dark[\s\S]*\.text-slate-500\s*\{\s*color:\s*#cbd5e1/i);
});
