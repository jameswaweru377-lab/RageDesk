import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  base: "./",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    target: "es2022",
    cssCodeSplit: false,
    // Keep the renderer fully offline: no CDN, fonts, API or telemetry dependencies.
    sourcemap: false,
  },
});
