export const manifests = [
    {
        type: "modal",
        alias: "thta-ai-prompt-modal",
        name: "AI Prompt Modal",
        element: () => import("../modals/prompt-modal.element.js"),
    },

      {
        type: "modal",
        alias: "thta-ai-image-prompt-modal",
        name: "AI Image Prompt Modal",
        element: () => import("../modals/image-prompt-modal.element.js"),
    }
];