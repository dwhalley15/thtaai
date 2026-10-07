export const manifests = [
    {
        type: "modal",
        alias: "hu-gen-prompt-modal",
        name: "AI Prompt Modal",
        element: () => import("../modals/prompt-modal.element.js"),
    },

      {
        type: "modal",
        alias: "hu-gen-image-prompt-modal",
        name: "AI Image Prompt Modal",
        element: () => import("../modals/image-prompt-modal.element.js"),
    }
];