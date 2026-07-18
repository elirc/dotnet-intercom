import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Builds a single self-contained IIFE (dist/netintercom-widget.js) that host sites
// embed with one script tag. React is bundled in; styles are injected at runtime.
export default defineConfig({
  plugins: [react()],
  define: { "process.env.NODE_ENV": JSON.stringify("production") },
  build: {
    lib: {
      entry: "src/main.tsx",
      name: "NetIntercomWidget",
      formats: ["iife"],
      fileName: () => "netintercom-widget.js",
    },
  },
  server: { port: 5174 },
});
