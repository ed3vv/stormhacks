const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");

const host = process.env.HOST || "127.0.0.1";
const configuredPort = Number(process.env.PORT);
const startingPort = Number.isInteger(configuredPort) && configuredPort > 0
  ? configuredPort
  : 8000;
const shouldTryAnotherPort = process.env.PORT === undefined;
const root = path.resolve(__dirname, "..");

const contentTypes = {
  ".css": "text/css; charset=utf-8",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".wasm": "application/wasm",
};

// The map editor posts a new layout here. Development only, and only ever this one file.
const layoutFile = path.join(root, "src", "game", "golf", "layout.js");

function saveLayout(request, response) {
  const chunks = [];
  request.on("data", (chunk) => {
    chunks.push(chunk);
    if (chunks.reduce((total, c) => total + c.length, 0) > 1e6) request.destroy();
  });
  request.on("end", () => {
    const body = Buffer.concat(chunks).toString("utf8");
    if (!body.includes("export const LAYOUT")) {
      response.writeHead(400).end("That does not look like a layout file");
      return;
    }
    fs.writeFile(layoutFile, body, (error) => {
      if (error) response.writeHead(500).end(error.message);
      else response.writeHead(200).end("Saved");
    });
  });
}

const server = http.createServer((request, response) => {
  let pathname;

  try {
    pathname = decodeURIComponent(new URL(request.url, `http://${host}`).pathname);
  } catch {
    response.writeHead(400).end("Bad request");
    return;
  }

  if (pathname === "/save-layout") {
    // GET is the editor's capability probe: it asks whether this server can write the file.
    if (request.method === "GET") {
      response.writeHead(200, { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" }).end("ready");
      return;
    }
    if (request.method !== "POST") {
      response.writeHead(405).end("Use POST");
      return;
    }
    saveLayout(request, response);
    return;
  }

  const relativePath = pathname === "/" ? "index.html" : pathname.replace(/^\/+/, "");
  const filePath = path.resolve(root, relativePath);

  if (filePath !== root && !filePath.startsWith(`${root}${path.sep}`)) {
    response.writeHead(403).end("Forbidden");
    return;
  }

  fs.stat(filePath, (statError, stats) => {
    if (statError || !stats.isFile()) {
      response.writeHead(404).end("Not found");
      return;
    }

    response.writeHead(200, {
      "Content-Type": contentTypes[path.extname(filePath).toLowerCase()] || "application/octet-stream",
      // this is a development server: a stale page in the browser is never what anyone wants
      "Cache-Control": "no-store",
    });
    fs.createReadStream(filePath).pipe(response);
  });
});

let activePort = startingPort;

server.on("error", (error) => {
  if (error.code === "EADDRINUSE" && shouldTryAnotherPort && activePort < 65535) {
    const unavailablePort = activePort;
    activePort += 1;
    console.warn(`Port ${unavailablePort} is already in use; trying ${activePort}...`);
    server.listen(activePort, host);
    return;
  }

  console.error(`Could not start the development server: ${error.message}`);
  process.exitCode = 1;
});

server.listen(activePort, host, () => {
  console.log(`Spherical Plane World is running at http://${host}:${activePort}/`);
  console.log("Press Ctrl+C to stop the server.");
});
