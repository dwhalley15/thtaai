import { UmbModalToken } from "@umbraco-cms/backoffice/modal";

export type PromptModalData = {
    prompt: string;
    mode?: 'text' | 'html';
};

export type PromptModalValue = {
    prompt: string;
    generated?: string;
};

export const HU_GEN_PROMPT_MODAL = new UmbModalToken<
    PromptModalData,
    PromptModalValue
>(
    "hu-gen-prompt-modal",
    {
        modal: {
            type: "dialog",
            size: "full",
        },
    }
);