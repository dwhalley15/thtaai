export const manifests = [
  {
    type: "section",
    alias: "hu-gen.Section",
    name: "AI Generator Section",
    meta: {
      label: "AI Generator",
      pathname: "ai-generator",
    },
  },
  {
    type: "sectionView",
    alias: "hu-gen.SectionView",
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
        match: "hu-gen.Section",
      },
    ],
  },
  {
    type: "sectionView",
    alias: "hu-gen.SectionView.Templates",
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
        match: "hu-gen.Section",
      },
    ],
  },
];