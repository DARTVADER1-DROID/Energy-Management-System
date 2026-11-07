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
            catch { /* Storage restrictions must not break the application UI. */ }
        }
    };

    const systemPrefersDark = () => {
        try { return window.matchMedia?.("(prefers-color-scheme: dark)").matches === true; }
        catch { return false; }
    };

    const syncThemeControls = requested => {
        document.querySelectorAll("[data-set-theme]").forEach(button => {
            const selected = button.dataset.setTheme === requested;
            button.setAttribute("aria-pressed", selected ? "true" : "false");
            button.classList.toggle("theme-option-active", selected);
        });
        const toggle = document.getElementById("theme-toggle");
        if (toggle) {
            const dark = root.dataset.theme === "dark";
            toggle.setAttribute("aria-label", dark ? "Switch to light theme" : "Switch to dark theme");
            toggle.setAttribute("title", dark ? "Switch to light theme" : "Switch to dark theme");
            const icon = toggle.querySelector("[aria-hidden]");
            if (icon) icon.textContent = dark ? "☀" : "◐";
        }
    };

    const applyTheme = requestedTheme => {
        const theme = validThemes.has(requestedTheme) ? requestedTheme : "system";
        const resolved = theme === "system" ? (systemPrefersDark() ? "dark" : "light") : theme;
        root.dataset.theme = resolved;
        root.classList.toggle("dark", resolved === "dark");
        const themeColor = document.querySelector('meta[name="theme-color"]');
        themeColor?.setAttribute("content", resolved === "dark" ? "#0d0b14" : "#7040d8");
        storage.set("ems-theme", theme);
        syncThemeControls(theme);
        return resolved;
    };

    const initialTheme = storage.get("ems-theme");
    applyTheme(validThemes.has(initialTheme) ? initialTheme : "system");

    try {
        const media = window.matchMedia?.("(prefers-color-scheme: dark)");
        media?.addEventListener?.("change", () => {
            if ((storage.get("ems-theme") || "system") === "system") applyTheme("system");
        });
    } catch { /* Theme switching remains available without matchMedia support. */ }

    document.getElementById("theme-toggle")?.addEventListener("click", () => {
        applyTheme(root.dataset.theme === "dark" ? "light" : "dark");
    });

    document.querySelectorAll("[data-set-theme]").forEach(button => {
        button.addEventListener("click", () => applyTheme(button.dataset.setTheme));
    });

    // Password inputs are always masked initially; the control only reveals the value on explicit request.
    document.querySelectorAll('input[type="password"]').forEach(input => {
        if (input.dataset.passwordToggleReady === "true") return;
        input.dataset.passwordToggleReady = "true";
        const wrapper = document.createElement("div");
        wrapper.className = "password-field";
        input.parentNode.insertBefore(wrapper, input);
        wrapper.appendChild(input);
        const button = document.createElement("button");
        button.type = "button";
        button.className = "password-toggle";
        button.setAttribute("aria-label", "Show password");
        button.setAttribute("aria-pressed", "false");
        button.setAttribute("title", "Show password");
        button.innerHTML = '<svg viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/></svg>';
        button.addEventListener("click", () => {
            const reveal = input.type === "password";
            input.type = reveal ? "text" : "password";
            button.setAttribute("aria-label", reveal ? "Hide password" : "Show password");
            button.setAttribute("title", reveal ? "Hide password" : "Show password");
            button.setAttribute("aria-pressed", reveal ? "true" : "false");
            input.focus();
        });
        wrapper.appendChild(button);
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