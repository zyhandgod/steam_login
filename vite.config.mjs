import { defineConfig } from "vite";
export default defineConfig({
  base: "./",
  build: {
    rollupOptions: {
      onwarn(warning, warn) {
        if (
          warning.code === "MODULE_LEVEL_DIRECTIVE" &&
          warning.message.includes("use client")
        )
          return;
        warn(warning);
      },
      output: {
        manualChunks(id) {
          if (id.includes("node_modules"))
            return /[\\/]node_modules[\\/](react|react-dom|scheduler)[\\/]/.test(
              id,
            )
              ? "react"
              : "components";
        },
      },
    },
  },
  server: { host: "127.0.0.1", port: 5173, strictPort: true },
});
