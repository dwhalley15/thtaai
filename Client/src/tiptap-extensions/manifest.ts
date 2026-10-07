export const manifests = [
    {
        type: 'tiptapExtension',
        alias: 'hu-gen.ai.extension',
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
        alias: 'hu-gen.ai.toolbar',
        name: 'AI Toolbar Button',
        api: () => import('./ai.tiptap-toolbar-api'),
        forExtensions: ['hu-gen.ai.extension'],
        meta: {
            alias: 'aiGenerate',
            icon: 'icon-autofill',
            label: 'Generate AI'
        }
    }
];