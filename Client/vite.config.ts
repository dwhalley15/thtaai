import { defineConfig } from "vite";
import fs from "node:fs";
import path from "node:path";

export default defineConfig({
  build: {
    outDir: "../wwwroot/App_Plugins/thtaai",
    emptyOutDir: true,

    lib: {
      entry: "src/bundle.manifests.ts",
      formats: ["es"],
      fileName: () => "thta-ai"
    },

    rollupOptions: {
      external: [/^@umbraco/],
      output: {
        entryFileNames: "thta-ai.js",
        chunkFileNames: "chunks/[name].[hash].js",
        assetFileNames: "assets/[name].[hash][extname]"
      }
    }
  },

  plugins: [
    {
      name: "update-umbraco-package",

      buildStart() {
        const packagePath = path.resolve(
          process.cwd(),
          "public/umbraco-package.json"
        );

        const umbracoPackage = JSON.parse(
          fs.readFileSync(packagePath, "utf-8")
        );

        const buildVersion = Date.now();

        umbracoPackage.extensions[0].js =
          `/App_Plugins/thtaai/thta-ai.js?v=${buildVersion}`;

        fs.writeFileSync(
          packagePath,
          JSON.stringify(umbracoPackage, null, 2)
        );

        console.log(
          `Updated manifest with build version ${buildVersion}`
        );
      }
    }
  ]
});