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
    assert.match(css, /html:not\(\.dark\)[\s\S]*\.text-slate-500\s*\{\s*color:\s*#625d70/i);
    assert.match(css, /html\.dark[\s\S]*\.text-slate-500\s*\{\s*color:\s*#b8b1c8/i);
});

test("form controls use contrasting theme-aware foreground, surface and placeholder colours", () => {
    const css = fs.readFileSync(path.join(__dirname, "../../src/EnergyManagement.Api/Web/Styles/tailwind.css"), "utf8");
    assert.match(css, /main input:not\(\[type=hidden\]\)[\s\S]*color:var\(--ems-text\)/);
    assert.match(css, /input::placeholder[\s\S]*color:/);
    assert.match(css, /html\.dark input::placeholder/);
    assert.match(css, /-webkit-text-fill-color:\s*var\(--ems-text\)/);
});

test("password fields are explicitly masked and have an accessible reveal control", () => {
    const login = fs.readFileSync(path.join(__dirname, "../../src/EnergyManagement.Api/Web/Pages/Account/Login.cshtml"), "utf8");
    const script = fs.readFileSync(path.join(__dirname, "../../src/EnergyManagement.Api/wwwroot/js/site.js"), "utf8");
    assert.match(login, /Input\.Password" type="password"/);
    assert.match(script, /input\[type="password"\]/);
    assert.match(script, /aria-pressed/);
    assert.match(script, /Hide password/);
});

test("application shell exposes both theme modes and preserves responsive navigation", () => {
    const layout = fs.readFileSync(path.join(__dirname, "../../src/EnergyManagement.Api/Web/Pages/Shared/_Layout.cshtml"), "utf8");
    assert.match(layout, /id="theme-toggle"/);
    assert.match(layout, /id="sidebar-toggle"/);
    assert.match(layout, /id="sidebar-backdrop"/);
});
