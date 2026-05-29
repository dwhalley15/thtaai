import { UmbModalToken } from "@umbraco-cms/backoffice/modal";

export type PromptModalData = {
    prompt: string;
    mode?: 'text' | 'html';
};

export type PromptModalValue = {
    prompt: string;
    generated?: string;
};

export const THTA_AI_PROMPT_MODAL = new UmbModalToken<
    PromptModalData,
    PromptModalValue
>(
    "thta-ai-prompt-modal",
    {
        modal: {
            type: "dialog",
            size: "full",
        },
    }
);