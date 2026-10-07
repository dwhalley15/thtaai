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

export const HU_GEN_IMAGE_PROMPT_MODAL = new UmbModalToken<
    ImagePromptModalData,
    ImagePromptModalValue
>(
    "hu-gen-image-prompt-modal",
    {
        modal: {
            type: "dialog",
            size: "full",
        },
    }
);