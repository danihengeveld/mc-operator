import { createRouter as createTanstackRouter } from "@tanstack/react-router";
import { routeTree } from "./routeTree.gen";

export function createRouter() {
  return createTanstackRouter({
    routeTree,
    scrollRestoration: true,
  });
}

let router: ReturnType<typeof createRouter>;

export function getRouter() {
  if (!router) {
    router = createRouter();
  }
  return router;
}

declare module "@tanstack/react-router" {
  interface Register {
    router: ReturnType<typeof createRouter>;
  }
}
