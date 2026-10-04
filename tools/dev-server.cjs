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

const server = http.createServer((request, response) => {
  let pathname;

  try {
    pathname = decodeURIComponent(new URL(request.url, `http://${host}`).pathname);
  } catch {
    response.writeHead(400).end("Bad request");
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
