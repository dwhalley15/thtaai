import { UmbTiptapExtensionApiBase } from '@umbraco-cms/backoffice/tiptap';
import { AiExtension } from './ai-toolbar.extension';

export default class AiTiptapApi extends UmbTiptapExtensionApiBase {
    getTiptapExtensions = () => [AiExtension];
}