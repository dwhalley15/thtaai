export const manifests = [
  {
    type: "section",
    alias: "thta_ai.Section",
    name: "AI Generator Section",
    meta: {
      label: "AI Generator",
      pathname: "ai-generator",
    },
  },
  {
    type: "sectionView",
    alias: "thta_ai.SectionView",
    name: "AI Generator Section View",
    element: () => import("./ai-generator.element"),
    meta: {
      label: "AI Page Generator",
      pathname: "ai-generator",
      icon: "icon-edit",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "thta_ai.Section",
      },
    ],
  },
  {
    type: "sectionView",
    alias: "thta_ai.SectionView.Templates",
    name: "Schema Generator View",
    element: () => import("./template-generator.element"),
    meta: {
      label: "Page Schema Generator",
      pathname: "templates",
      icon: "icon-palette",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "thta_ai.Section",
      },
    ],
  },
];