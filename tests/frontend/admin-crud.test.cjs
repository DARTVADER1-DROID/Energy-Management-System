const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const script = fs.readFileSync(path.join(__dirname, "../../src/EnergyManagement.Api/wwwroot/js/admin-crud.js"), "utf8");

function element({ textContent = "", value = "", status = "", tagName = "BUTTON", buttons = [] } = {}) {
    const listeners = {};
    return {
        textContent, value, hidden: false, disabled: false, tagName,
        dataset: status ? { status } : {},
        listeners,
        addEventListener(name, callback) { (listeners[name] ??= []).push(callback); },
        dispatch(name, event = {}) {
            event.preventDefault ??= () => { event.defaultPrevented = true; };
            for (const callback of listeners[name] ?? []) callback(event);
            return event;
        },
        querySelectorAll() { return buttons; }
    };
}

function boot({ rows = [], searchValue = "", statusValue = "all", confirmResult = true } = {}) {
    const search = element({ value: searchValue });
    const status = element({ value: statusValue });
    const count = element();
    const empty = element();
    const table = element();
    table.dataset = { adminTable: "clients" };
    table.querySelectorAll = () => rows;
    const form = element();
    form.dataset = {};
    const submitButton = element({ textContent: "Save", tagName: "BUTTON" });
    form.querySelectorAll = () => [submitButton];
    const selectors = {
        '[data-admin-search="clients"]': search,
        '[data-admin-status-filter="clients"]': status,
        '[data-admin-visible-count="clients"]': count,
        '[data-admin-empty="clients"]': empty
    };
    const document = {
        readyState: "complete",
        querySelectorAll(selector) {
            if (selector === "[data-admin-table]") return [table];
            if (selector === "form[data-admin-submit]") return [form];
            return [];
        },
        querySelector(selector) { return selectors[selector] ?? null; },
        addEventListener() {}
    };
    const window = { confirm: () => confirmResult };
    vm.runInNewContext(script, { document, window });
    return { search, status, count, empty, table, form, submitButton, window };
}

test("search filters rows case-insensitively and reports visible count", () => {
    const rows = [element({ textContent: "Alice Example", status: "active" }), element({ textContent: "Bob Example", status: "disabled" })];
    const app = boot({ rows });
    app.search.value = "BOB";
    app.search.dispatch("input");
    assert.equal(rows[0].hidden, true);
    assert.equal(rows[1].hidden, false);
    assert.equal(app.count.textContent, "1 of 2 shown");
});

test("search normalization handles accents and surrounding whitespace", () => {
    const rows = [element({ textContent: "José García", status: "active" })];
    const app = boot({ rows });
    app.search.value = "  jose  ";
    app.search.dispatch("input");
    assert.equal(rows[0].hidden, false);
});

test("status filter and search combine rather than override each other", () => {
    const rows = [
        element({ textContent: "Alice", status: "active" }),
        element({ textContent: "Alice", status: "disabled" }),
        element({ textContent: "Bob", status: "disabled" })
    ];
    const app = boot({ rows });
    app.search.value = "alice";
    app.status.value = "disabled";
    app.search.dispatch("input");
    app.status.dispatch("change");
    assert.deepEqual(rows.map(row => row.hidden), [true, false, true]);
    assert.equal(app.count.textContent, "1 of 3 shown");
});

test("empty state appears when filters exclude every row", () => {
    const rows = [element({ textContent: "Alice", status: "active" })];
    const app = boot({ rows });
    app.search.value = "not present";
    app.search.dispatch("input");
    assert.equal(app.empty.hidden, false);
    app.search.value = "";
    app.search.dispatch("input");
    assert.equal(app.empty.hidden, true);
});

test("destructive form is cancelled when confirmation is declined", () => {
    const app = boot({ confirmResult: false });
    app.form.dataset.adminConfirm = "Delete?";
    const event = app.form.dispatch("submit");
    assert.equal(event.defaultPrevented, true);
    assert.equal(app.form.dataset.submitting, undefined);
    assert.equal(app.submitButton.disabled, false);
});

test("accepted submission disables controls and changes the button label", () => {
    const app = boot();
    app.form.dispatch("submit");
    assert.equal(app.form.dataset.submitting, "true");
    assert.equal(app.submitButton.disabled, true);
    assert.equal(app.submitButton.textContent, "Working…");
});

test("duplicate submissions are blocked", () => {
    const app = boot();
    app.form.dispatch("submit");
    const second = app.form.dispatch("submit");
    assert.equal(second.defaultPrevented, true);
});

test("normalizer is available and safe for null input", () => {
    const context = { document: { readyState: "loading", addEventListener() {}, querySelectorAll() { return []; } }, window: {} };
    vm.runInNewContext(script, context);
    assert.equal(context.window.EMSAdminCrud.normalize(null), "");
    assert.equal(context.window.EMSAdminCrud.normalize("  ÀBC  "), "abc");
});
