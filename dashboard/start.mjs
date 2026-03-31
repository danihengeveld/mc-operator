import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { join, extname } from "node:path";
import { fileURLToPath } from "node:url";

const port = parseInt(process.env.PORT || "3000", 10);
const host = process.env.HOST || "0.0.0.0";

const __dirname = fileURLToPath(new URL(".", import.meta.url));
const clientDir = join(__dirname, "dist", "client");

const MIME_TYPES = {
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

async function serveStaticFile(filePath, res) {
  try {
    const stats = await stat(filePath);
    if (!stats.isFile()) return false;

    const ext = extname(filePath);
    const contentType = MIME_TYPES[ext] || "application/octet-stream";
    const data = await readFile(filePath);

    const headers = {
      "Content-Type": contentType,
      "Content-Length": data.length,
    };

    // Cache assets with hashes for 1 year
    if (filePath.includes("/assets/")) {
      headers["Cache-Control"] = "public, max-age=31536000, immutable";
    }

    res.writeHead(200, headers);
    res.end(data);
    return true;
  } catch {
    return false;
  }
}

async function start() {
  const { default: app } = await import("./dist/server/server.js");

  const server = createServer(async (req, res) => {
    try {
      const url = new URL(req.url || "/", `http://${req.headers.host || "localhost"}`);

      // Try to serve static files from dist/client first
      const staticPath = join(clientDir, url.pathname);
      if (url.pathname !== "/" && await serveStaticFile(staticPath, res)) {
        return;
      }

      // Build a standard Request from the Node.js IncomingMessage
      const protocol = req.headers["x-forwarded-proto"] || "http";
      const requestUrl = new URL(req.url || "/", `${protocol}://${req.headers.host || "localhost"}`);

      const headers = new Headers();
      for (const [key, value] of Object.entries(req.headers)) {
        if (value) {
          if (Array.isArray(value)) {
            for (const v of value) headers.append(key, v);
          } else {
            headers.set(key, value);
          }
        }
      }

      const hasBody = req.method !== "GET" && req.method !== "HEAD";
      const request = new Request(requestUrl.toString(), {
        method: req.method,
        headers,
        body: hasBody ? req : undefined,
        // @ts-expect-error duplex is needed for readable stream bodies
        duplex: hasBody ? "half" : undefined,
      });

      const response = await app.fetch(request);

      // Collect response headers
      const responseHeaders = {};
      response.headers.forEach((value, key) => {
        const existing = responseHeaders[key];
        if (existing) {
          responseHeaders[key] = Array.isArray(existing)
            ? [...existing, value]
            : [existing, value];
        } else {
          responseHeaders[key] = value;
        }
      });

      res.writeHead(response.status, response.statusText, responseHeaders);

      if (response.body) {
        const reader = response.body.getReader();
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;
          res.write(value);
        }
      }
      res.end();
    } catch (error) {
      console.error("Request error:", error);
      if (!res.headersSent) {
        res.writeHead(500, { "Content-Type": "text/plain" });
      }
      res.end("Internal Server Error");
    }
  });

  server.listen(port, host, () => {
    console.log(`mc-operator Dashboard listening on http://${host}:${port}`);
  });
}

start().catch((error) => {
  console.error("Failed to start dashboard:", error);
  process.exit(1);
});
