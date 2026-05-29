import { Extension } from '@tiptap/core';

declare module '@tiptap/core' {
    interface Commands<ReturnType> {
        aiExtension: {
            insertAiText: (text: string) => ReturnType;
        };
    }
}

export const AiExtension = Extension.create({
    name: 'aiExtension',

    addCommands() {
        return {
            insertAiText:
                (text: string) =>
                ({ chain }) => {
                    return chain()
                        .focus()
                        .insertContent(text)
                        .run();
                },
        };
    },
});