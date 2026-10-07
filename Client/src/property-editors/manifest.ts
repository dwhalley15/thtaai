import type { ManifestPropertyEditorUi } from '@umbraco-cms/backoffice/property-editor';

export const manifests: ManifestPropertyEditorUi[] = [
    {
        type: "propertyEditorUi",
        alias: "hu-gen.propertyEditor.aiTextstring",
        name: "AI Textstring",

        elementName: "hu-gen-textstring",
        element: () => import("./ai-textstring.element"),

        meta: {
            label: "AI Textstring",
            icon: "icon-autofill",
            group: "ai-wrappers",
            propertyEditorSchemaAlias: "Umbraco.TextBox",
        },
    },

    {
        type: "propertyEditorUi",
        alias: "hu-gen.propertyEditor.aiTextarea",
        name: "AI Textarea",

        elementName: "hu-gen-textarea",
        element: () => import("./ai-textarea.element"),

        meta: {
            label: "AI Textarea",
            icon: "icon-article",
            group: "ai-wrappers",
            propertyEditorSchemaAlias: "Umbraco.TextArea",
        },
    },

    {
        type: "propertyEditorUi",
        alias: "hu-gen.propertyEditor.aiImage",
        name: "AI Image",

        elementName: "hu-gen-ai-image",
        element: () => import("./ai-image.element"),

        meta: {
            label: "AI Image",
            icon: "icon-picture",
            group: "ai-wrappers",
            propertyEditorSchemaAlias: "Umbraco.MediaPicker3",
            supportsReadOnly: true,
        },

    }
];