import { createReadStream } from "node:fs";
import { access, stat } from "node:fs/promises";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const siteRoot = path.resolve(__dirname, "..", "site");
const host = process.env.LOCAL_SITE_HOST || "localhost";
const port = Number(process.env.LOCAL_SITE_PORT || 8080);
const apiTarget = process.env.LOCAL_API_TARGET || "http://localhost:5010";

const contentTypes = new Map([
  [".css", "text/css; charset=utf-8"],
  [".html", "text/html; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".svg", "image/svg+xml"],
  [".png", "image/png"],
  [".jpg", "image/jpeg"],
  [".jpeg", "image/jpeg"],
  [".ico", "image/x-icon"]
]);

function send(response, status, body, headers = {}) {
  response.writeHead(status, {
    "cache-control": "no-store",
    ...headers
  });
  response.end(body);
}

function proxyApi(request, response) {
  const target = new URL(request.url, apiTarget);
  const proxyRequest = http.request(
    target,
    {
      method: request.method,
      headers: {
        ...request.headers,
        host: target.host
      }
    },
    (proxyResponse) => {
      response.writeHead(proxyResponse.statusCode || 502, proxyResponse.headers);
      proxyResponse.pipe(response);
    }
  );

  proxyRequest.on("error", () => {
    send(response, 502, "API local nao respondeu. Confirme se ela esta em http://localhost:5010.");
  });

  request.pipe(proxyRequest);
}

function isInsideSiteRoot(filePath) {
  const relativePath = path.relative(siteRoot, filePath);
  return relativePath === "" || (!relativePath.startsWith("..") && !path.isAbsolute(relativePath));
}

async function resolveStaticFile(urlPath) {
  let decodedPath;
  try {
    decodedPath = decodeURIComponent(urlPath.split("?")[0]);
  } catch {
    return null;
  }

  const normalized = path.normalize(decodedPath).replace(/^[/\\]+/, "");
  let filePath = path.resolve(siteRoot, normalized);

  if (!isInsideSiteRoot(filePath)) {
    return null;
  }

  try {
    const info = await stat(filePath);
    if (info.isDirectory()) {
      filePath = path.join(filePath, "index.html");
    }
  } catch {
    if (!path.extname(filePath)) {
      filePath = path.join(filePath, "index.html");
    }
  }

  if (!isInsideSiteRoot(filePath)) {
    return null;
  }

  await access(filePath);
  return filePath;
}

const server = http.createServer(async (request, response) => {
  if (request.url?.startsWith("/api/")) {
    proxyApi(request, response);
    return;
  }

  try {
    const filePath = await resolveStaticFile(request.url || "/");
    if (!filePath) {
      send(response, 403, "Forbidden");
      return;
    }

    const type = contentTypes.get(path.extname(filePath).toLowerCase()) || "application/octet-stream";
    response.writeHead(200, {
      "cache-control": "no-store",
      "content-type": type
    });
    createReadStream(filePath).pipe(response);
  } catch {
    send(response, 404, "Not found");
  }
});

server.listen(port, host, () => {
  console.log(`Site local: http://${host}:${port}/`);
  console.log(`API proxy: ${apiTarget}/api/*`);
});
