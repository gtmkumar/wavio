import { defineConfig } from "vitest/config";
import path from "node:path";

// Deliberately separate from vite.config.ts: the unit tests exercise plain
// modules (src/api/http.ts), so they don't need the router/react/tailwind
// plugins — or a browser DOM. Vitest prefers this file over vite.config.ts.
export default defineConfig({
  resolve: {
    alias: { "@": path.resolve(import.meta.dirname, "src") },
  },
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
});
