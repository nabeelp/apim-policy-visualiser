import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": "http://localhost:5021",
      "/healthz": "http://localhost:5021",
    },
  },
  test: {
    environment: "jsdom",
    passWithNoTests: false,
    setupFiles: "./src/test/setup.ts",
  },
});
