import { UmbModalToken } from "@umbraco-cms/backoffice/modal";

export type ImagePromptModalData = {
    prompt: string;
};

export type ImageGenerateResponse = {
  mediaUrl: string;
  sourceUrl: string;
  previewUrl: string;
  altText: string;
  title: string;
};

export type ImagePromptModalValue = {
    mediaKey: string;
    url: string;
    altText: string;
}

export const THTA_AI_IMAGE_PROMPT_MODAL = new UmbModalToken<
    ImagePromptModalData,
    ImagePromptModalValue
>(
    "thta-ai-image-prompt-modal",
    {
        modal: {
            type: "dialog",
            size: "full",
        },
    }
);