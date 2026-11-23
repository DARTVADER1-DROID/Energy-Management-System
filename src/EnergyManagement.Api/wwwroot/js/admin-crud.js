(() => {
    "use strict";

    const normalize = (value) => String(value ?? "")
        .normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .toLocaleLowerCase()
        .trim();

    function initializeTable(table) {
        const key = table.dataset.adminTable;
        if (!key) return;

        const rows = Array.from(table.querySelectorAll("tbody [data-admin-row]"));
        const search = document.querySelector('[data-admin-search="' + key + '"]');
        const status = document.querySelector('[data-admin-status-filter="' + key + '"]');
        const count = document.querySelector('[data-admin-visible-count="' + key + '"]');
        const empty = document.querySelector('[data-admin-empty="' + key + '"]');

        const apply = () => {
            const query = normalize(search?.value);
            const selectedStatus = normalize(status?.value || "all");
            let visible = 0;

            for (const row of rows) {
                const text = normalize(row.textContent);
                const rowStatus = normalize(row.dataset.status || "");
                const matchesSearch = query.length === 0 || text.includes(query);
                const matchesStatus = selectedStatus === "all" || rowStatus === selectedStatus;
                row.hidden = !(matchesSearch && matchesStatus);
                if (!row.hidden) visible++;
            }

            if (count) count.textContent = visible + " of " + rows.length + " shown";
            if (empty) empty.hidden = visible !== 0;
            table.dataset.visibleRows = String(visible);
        };

        search?.addEventListener("input", apply);
        status?.addEventListener("change", apply);
        apply();
    }

    function initializeForms() {
        document.querySelectorAll("form[data-admin-submit]").forEach((form) => {
            form.addEventListener("submit", (event) => {
                if (form.dataset.submitting === "true") {
                    event.preventDefault();
                    return;
                }

                const confirmation = form.dataset.adminConfirm;
                if (confirmation && !window.confirm(confirmation)) {
                    event.preventDefault();
                    return;
                }

                form.dataset.submitting = "true";
                form.querySelectorAll('button:not([type]), button[type="submit"], input[type="submit"]').forEach((button) => {
                    button.disabled = true;
                    if (button.tagName === "BUTTON" && !button.dataset.originalText) {
                        button.dataset.originalText = button.textContent;
                        button.textContent = "Working…";
                    }
                });
            });
        });
    }

    function initialize() {
        document.querySelectorAll("[data-admin-table]").forEach(initializeTable);
        initializeForms();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }

    window.EMSAdminCrud = Object.freeze({ normalize });
})();
