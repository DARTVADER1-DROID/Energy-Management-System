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
})();
