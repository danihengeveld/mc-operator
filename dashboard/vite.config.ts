import { tanstackStart } from "@tanstack/react-start/plugin/vite";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";
import { nitro } from "nitro/vite";

export default defineConfig({
  plugins: [
    tailwindcss(),
    ...tanstackStart({
      srcDirectory: "app",
    }),
    nitro({
      preset: "bun",
    }),
  ],
});
