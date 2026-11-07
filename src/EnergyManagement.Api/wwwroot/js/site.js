(() => {
    "use strict";

    const root = document.documentElement;
    const validThemes = new Set(["light", "dark", "system"]);
    const storage = {
        get(key) {
            try { return window.localStorage?.getItem(key) ?? null; }
            catch { return null; }
        },
        set(key, value) {
            try { window.localStorage?.setItem(key, value); }
            catch { /* Private browsing or storage restrictions must not break the application UI. */ }
        }
    };

    const systemPrefersDark = () => {
        try { return window.matchMedia?.("(prefers-color-scheme: dark)").matches === true; }
        catch { return false; }
    };

    const applyTheme = requestedTheme => {
        const theme = validThemes.has(requestedTheme) ? requestedTheme : "system";
        const resolved = theme === "system" ? (systemPrefersDark() ? "dark" : "light") : theme;
        root.dataset.theme = resolved;
        root.classList.toggle("dark", resolved === "dark");
        const themeColor = document.querySelector('meta[name="theme-color"]');
        themeColor?.setAttribute("content", resolved === "dark" ? "#171326" : "#6d28d9");
        storage.set("ems-theme", theme);
        return resolved;
    };

    const initialTheme = storage.get("ems-theme");
    applyTheme(validThemes.has(initialTheme) ? initialTheme : "system");

    try {
        const media = window.matchMedia?.("(prefers-color-scheme: dark)");
        media?.addEventListener?.("change", () => {
            if ((storage.get("ems-theme") || "system") === "system") applyTheme("system");
        });
    } catch { /* Theme switching remains available even without matchMedia support. */ }

    document.getElementById("theme-toggle")?.addEventListener("click", () => {
        applyTheme(root.dataset.theme === "dark" ? "light" : "dark");
    });

    document.querySelectorAll("[data-set-theme]").forEach(button => {
        button.addEventListener("click", () => applyTheme(button.dataset.setTheme));
    });

    const sidebar = document.getElementById("sidebar");
    const backdrop = document.getElementById("sidebar-backdrop");
    const toggle = document.getElementById("sidebar-toggle");
    const closeSidebar = () => {
        sidebar?.classList.add("-translate-x-full");
        backdrop?.classList.add("hidden");
        toggle?.setAttribute("aria-expanded", "false");
    };
    toggle?.addEventListener("click", () => {
        const isOpen = toggle.getAttribute("aria-expanded") === "true";
        if (isOpen) closeSidebar();
        else {
            sidebar?.classList.remove("-translate-x-full");
            backdrop?.classList.remove("hidden");
            toggle?.setAttribute("aria-expanded", "true");
        }
    });
    backdrop?.addEventListener("click", closeSidebar);
    document.addEventListener("keydown", event => {
        if (event.key === "Escape") closeSidebar();
    });

    const wizard = document.querySelector("[data-registration-wizard]");
    if (wizard) {
        const steps = [...wizard.querySelectorAll("[data-wizard-step]")];
        const indicators = [...wizard.querySelectorAll("[data-wizard-indicator]")];
        if (steps.length > 0) {
            let activeStep = 0;
            const showStep = index => {
                activeStep = Math.max(0, Math.min(index, steps.length - 1));
                steps.forEach((step, i) => { step.hidden = i !== activeStep; });
                indicators.forEach((indicator, i) => {
                    indicator.setAttribute("aria-current", i === activeStep ? "step" : "false");
                    indicator.classList.toggle("wizard-step-active", i === activeStep);
                });
                wizard.querySelector("[data-wizard-back]")?.toggleAttribute("hidden", activeStep === 0);
                const next = wizard.querySelector("[data-wizard-next]");
                const submit = wizard.querySelector("[data-wizard-submit]");
                if (next) next.hidden = activeStep === steps.length - 1;
                if (submit) submit.hidden = activeStep !== steps.length - 1;
                if (activeStep === steps.length - 1) {
                    const value = id => document.getElementById(id)?.value || "—";
                    const text = (id, content) => {
                        const target = document.getElementById(id);
                        if (target) target.textContent = content;
                    };
                    text("review-name", value("Input_Name"));
                    text("review-hardware", value("Input_HardwareModel"));
                    text("review-location", value("Input_InstallationLocation"));
                    text("review-electrical", wizard.querySelector('input[name="Input.ElectricalConfiguration"]:checked')?.value || "—");
                }
            };

            const findInvalidField = index => [...steps[index].querySelectorAll("input, select, textarea")]
                .find(field => field.willValidate && !field.checkValidity());
            const validateStep = index => {
                const invalid = findInvalidField(index);
                if (!invalid) return true;
                invalid.reportValidity();
                invalid.focus();
                return false;
            };

            wizard.querySelector("[data-wizard-next]")?.addEventListener("click", () => {
                if (validateStep(activeStep)) showStep(activeStep + 1);
            });
            wizard.querySelector("[data-wizard-back]")?.addEventListener("click", () => showStep(activeStep - 1));
            wizard.addEventListener("submit", event => {
                for (let index = 0; index < steps.length; index++) {
                    const invalid = findInvalidField(index);
                    if (invalid) {
                        event.preventDefault();
                        showStep(index);
                        invalid.reportValidity();
                        invalid.focus();
                        return;
                    }
                }
            });
            showStep(0);
        }
    }
})();
