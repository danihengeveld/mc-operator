/**
 * TanStack Start Production Server with Bun
 *
 * Based on https://bun.com/docs/guides/ecosystem/tanstack-start#hosting
 *
 * Serves the TanStack Start application with static asset preloading
 * and Bun's built-in HTTP server.
 */

import path from "node:path";

const SERVER_PORT = Number(process.env.PORT ?? 3000);
const CLIENT_DIRECTORY = "./dist/client";
const SERVER_ENTRY_POINT = "./dist/server/server.js";

const MIME_TYPES: Record<string, string> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".svg": "image/svg+xml",
  ".ico": "image/x-icon",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
  ".map": "application/json",
};

interface StaticRoute {
  (req: Request): Response;
}

/**
 * Preload small static assets into memory; serve large files on-demand.
 */
async function initializeStaticRoutes(
  clientDirectory: string,
): Promise<Record<string, StaticRoute>> {
  const routes: Record<string, StaticRoute> = {};
  const maxPreloadBytes = 5 * 1024 * 1024; // 5 MB

  try {
    const glob = new Bun.Glob("**/*");
    for await (const relativePath of glob.scan({ cwd: clientDirectory })) {
      const filepath = path.join(clientDirectory, relativePath);
      const route = `/${relativePath.split(path.sep).join(path.posix.sep)}`;

      const file = Bun.file(filepath);
      if (!(await file.exists()) || file.size === 0) continue;

      const ext = path.extname(filepath);
      const mimeType = MIME_TYPES[ext] ?? file.type ?? "application/octet-stream";
      const immutable = filepath.includes("/assets/");

      if (file.size <= maxPreloadBytes) {
        const data = new Uint8Array(await file.arrayBuffer());
        routes[route] = (_req: Request) =>
          new Response(new Uint8Array(data), {
            headers: {
              "Content-Type": mimeType,
              "Content-Length": String(data.byteLength),
              "Cache-Control": immutable
                ? "public, max-age=31536000, immutable"
                : "public, max-age=3600",
            },
          });
      } else {
        routes[route] = (_req: Request) =>
          new Response(Bun.file(filepath), {
            headers: {
              "Content-Type": mimeType,
              "Cache-Control": "public, max-age=3600",
            },
          });
      }
    }
  } catch (error) {
    console.error(
      `Failed to load static files from ${clientDirectory}:`,
      error,
    );
  }

  return routes;
}

async function main() {
  // Load TanStack Start server handler
  const { default: app } = (await import(SERVER_ENTRY_POINT)) as {
    default: { fetch: (request: Request) => Response | Promise<Response> };
  };

  const routes = await initializeStaticRoutes(CLIENT_DIRECTORY);

  const server = Bun.serve({
    port: SERVER_PORT,
    routes: {
      ...routes,
      "/*": (req: Request) => {
        try {
          return app.fetch(req);
        } catch (error) {
          console.error("Server handler error:", error);
          return new Response("Internal Server Error", { status: 500 });
        }
      },
    },
    error(error) {
      console.error("Uncaught server error:", error);
      return new Response("Internal Server Error", { status: 500 });
    },
  });

  console.log(
    `mc-operator Dashboard listening on http://localhost:${String(server.port)}`,
  );
}

main().catch((error) => {
  console.error("Failed to start dashboard:", error);
  process.exit(1);
});
