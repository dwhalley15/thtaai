export const manifests = [
    {
        type: 'tiptapExtension',
        alias: 'thta.ai.extension',
        name: 'AI TipTap Extension',
        api: () => import('./ai.tiptap-api'),
        meta: {
            icon: 'icon-autofill',
            label: 'AI',
            group: '#tiptap_extGroup_formatting'
        }
    },
    {
        type: 'tiptapToolbarExtension',
        kind: 'button',                         
        alias: 'thta.ai.toolbar',
        name: 'AI Toolbar Button',
        api: () => import('./ai.tiptap-toolbar-api'),
        forExtensions: ['thta.ai.extension'],
        meta: {
            alias: 'aiGenerate',
            icon: 'icon-autofill',
            label: 'Generate AI'
        }
    }
];