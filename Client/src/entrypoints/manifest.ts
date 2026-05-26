export const manifests = [
  {
    name: "thtaaiEntrypoint",
    alias: "thta_ai.Entrypoint",
    type: "backofficeEntryPoint",
    js: () => import("./entrypoint.js"),
  },

];