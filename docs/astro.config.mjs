import { defineConfig } from "astro/config";
import starlight from "@astrojs/starlight";
import cloudflare from "@astrojs/cloudflare";

export default defineConfig({
  site: "https://mc-operator.dhv.sh/",
  base: "/",
  adapter: cloudflare(),
  integrations: [
    starlight({
      title: "mc-operator",
      description:
        "Kubernetes Operator for managing Minecraft server deployments",
      logo: {
        light: "./src/assets/logo-light.svg",
        dark: "./src/assets/logo-dark.svg",
        replacesTitle: false,
      },
      social: [
        {
          icon: "github",
          label: "GitHub",
          href: "https://github.com/danihengeveld/mc-operator",
        },
      ],
      editLink: {
        baseUrl:
          "https://github.com/danihengeveld/mc-operator/edit/main/docs/",
      },
      customCss: ["./src/styles/custom.css"],
      sidebar: [
        {
          label: "Getting Started",
          items: [
            { label: "Introduction", slug: "getting-started/introduction" },
            { label: "Quickstart", slug: "getting-started/quickstart" },
            { label: "Installation", slug: "getting-started/installation" },
          ],
        },
        {
          label: "Reference",
          items: [
            { label: "CRD Reference", slug: "reference/crd" },
            { label: "Configuration Guide", slug: "reference/configuration" },
            { label: "Examples", slug: "reference/examples" },
          ],
        },
        {
          label: "Guides",
          items: [
            { label: "Architecture", slug: "guides/architecture" },
            {
              label: "Running in Production",
              slug: "guides/production",
            },
            { label: "Troubleshooting & FAQ", slug: "guides/troubleshooting" },
          ],
        },
        {
          label: "Development",
          items: [
            { label: "Development Guide", slug: "development/guide" },
            { label: "Contributing", slug: "development/contributing" },
            { label: "Release Process", slug: "development/releases" },
          ],
        },
        {
          label: "About",
          items: [
            { label: "Roadmap", slug: "about/roadmap" },
          ],
        },
      ],
      expressiveCode: {
        themes: ["starlight-dark", "starlight-light"],
        defaultProps: {
          wrap: true,
        },
      },
      head: [
        {
          tag: "meta",
          attrs: {
            property: "og:type",
            content: "website",
          },
        },
        {
          tag: "meta",
          attrs: {
            name: "og:site_name",
            content: "mc-operator",
          },
        },
        {
          tag: "meta",
          attrs: {
            name: "twitter:card",
            content: "summary_large_image",
          },
        },
      ],
    }),
  ]
});
