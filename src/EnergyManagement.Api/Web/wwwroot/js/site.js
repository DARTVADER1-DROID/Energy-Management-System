(() => {
    const root = document.documentElement;
    const applyTheme = (theme) => {
        const resolved = theme === "system"
            ? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")
            : theme;
        root.dataset.theme = resolved;
        root.classList.toggle("dark", resolved === "dark");
        localStorage.setItem("ems-theme", theme);
    };

    const storedTheme = localStorage.getItem("ems-theme") || "system";
    applyTheme(storedTheme);
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
    };
    toggle?.addEventListener("click", () => {
        sidebar?.classList.remove("-translate-x-full");
        backdrop?.classList.remove("hidden");
    });
    backdrop?.addEventListener("click", closeSidebar);

    const wizard = document.querySelector("[data-registration-wizard]");
    if (wizard) {
        const steps = [...wizard.querySelectorAll("[data-wizard-step]")];
        const indicators = [...wizard.querySelectorAll("[data-wizard-indicator]")];
        let activeStep = 0;
        const showStep = (index) => {
            activeStep = Math.max(0, Math.min(index, steps.length - 1));
            steps.forEach((step, i) => step.hidden = i !== activeStep);
            indicators.forEach((indicator, i) => {
                indicator.setAttribute("aria-current", i === activeStep ? "step" : "false");
                indicator.classList.toggle("wizard-step-active", i === activeStep);
            });
            wizard.querySelector("[data-wizard-back]")?.toggleAttribute("hidden", activeStep === 0);
            const next = wizard.querySelector("[data-wizard-next]");
            const submit = wizard.querySelector("[data-wizard-submit]");
            if (next) next.hidden = activeStep === steps.length - 1;
            if (submit) submit.hidden = activeStep !== steps.length - 1;
        };
        wizard.querySelector("[data-wizard-next]")?.addEventListener("click", () => {
            const invalid = [...steps[activeStep].querySelectorAll("input, select, textarea")]
                .find(field => !field.checkValidity());
            if (invalid) { invalid.reportValidity(); return; }
            showStep(activeStep + 1);
        });
        wizard.querySelector("[data-wizard-back]")?.addEventListener("click", () => showStep(activeStep - 1));
        showStep(0);
    }
})();
