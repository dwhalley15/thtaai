import { UmbTiptapToolbarElementApiBase } from '@umbraco-cms/backoffice/tiptap';
import type { Editor } from '@umbraco-cms/backoffice/tiptap';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { THTA_AI_PROMPT_MODAL } from '../modals/prompt-modal.token';

export default class AiToolbarApi extends UmbTiptapToolbarElementApiBase {

    override async execute(editor?: Editor): Promise<void> {
        if (!editor) return;

        const modalManager = await new Promise<any>((resolve) => {
            this.consumeContext(UMB_MODAL_MANAGER_CONTEXT, (instance) => {
                resolve(instance);
            });
        });

        if (!modalManager) return;

        const modalHandler = modalManager.open(
            this,
            THTA_AI_PROMPT_MODAL,
            { data: { prompt: '', mode: 'html' } }
        );

        if (!modalHandler) return;

       let result;
        try {
            result = await modalHandler.onSubmit();
        } catch {
            return;
        }

        if (!result?.generated) return;

        editor.chain().focus().setContent(result.generated).run();
    }
}