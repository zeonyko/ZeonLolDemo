/**
 * 本地一键：VersionCheck 调度 + CDN 静态文件（同一个进程）。
 *
 * 不要再开第二个 http.server。线上才拆「调度域名 / CDN 域名」；
 * 本机用两个 URL 前缀即可：
 *   POST/GET  /version-check        调度 API（cdn_host + game_server_*）
 *   GET       /version_check.json   同上
 *   GET       /Manifests/*  /Bundles/*   模拟 CDN
 *
 * 用法：双击 local_dev_server-启动本地热更服务.bat
 * 包内引导：StreamingAssets/Launch/boot_config.json
 * 本机覆盖：Tools/local_dev.json（check_url / server_url / cdn_host）
 */
const http = require("http");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { URL } = require("url");

const REPO_ROOT = path.resolve(__dirname, "..");
const CDN_PARENT = path.join(REPO_ROOT, "CDN");
const MANIFEST_NAME_RE = /^manifest_.+_([0-9a-fA-F]+)\.bytes$/i;

const PLATFORM_FOLDERS = {
  Android: "Android",
  IPhonePlayer: "iOS",
  WindowsPlayer: "StandaloneWindows64",
  WindowsEditor: "StandaloneWindows64",
  OSXPlayer: "StandaloneOSX",
  OSXEditor: "StandaloneOSX",
  LinuxPlayer: "StandaloneLinux64",
  LinuxEditor: "StandaloneLinux64",
  WebGLPlayer: "WebGL",
  StandaloneWindows64: "StandaloneWindows64",
  StandaloneWindows: "StandaloneWindows64",
  iOS: "iOS",
  WebGL: "WebGL",
};

function parseArgs(argv) {
  const args = {
    host: "0.0.0.0",
    port: 8080,
    portFromCli: false,
    cdn: "",
    noUpdate: false,
    forceUpdate: false,
    help: false,
  };
  for (let i = 2; i < argv.length; i++) {
    const key = argv[i];
    const next = argv[i + 1];
    if (key === "--host" && next) {
      args.host = next;
      i++;
    } else if (key === "--port" && next) {
      args.port = Number(next);
      args.portFromCli = true;
      i++;
    } else if (key === "--cdn" && next) {
      args.cdn = path.resolve(next);
      i++;
    } else if (key === "--no-update") {
      args.noUpdate = true;
    } else if (key === "--force-update") {
      args.forceUpdate = true;
    } else if (key === "--help" || key === "-h") {
      args.help = true;
    }
  }
  return args;
}

function listPlatformDirs() {
  if (!fs.existsSync(CDN_PARENT) || !fs.statSync(CDN_PARENT).isDirectory())
    return [];
  return fs
    .readdirSync(CDN_PARENT)
    .map((name) => ({ name, full: path.join(CDN_PARENT, name) }))
    .filter((item) => {
      try {
        return fs.statSync(item.full).isDirectory();
      } catch (_) {
        return false;
      }
    });
}

function folderForPlatform(raw) {
  const key = String(raw || "").trim();
  if (PLATFORM_FOLDERS[key]) return PLATFORM_FOLDERS[key];
  if (key && fs.existsSync(path.join(CDN_PARENT, key))) return key;
  const ua = String(raw || "");
  if (/android/i.test(ua)) return "Android";
  if (/iphone|ipad|ios/i.test(ua)) return "iOS";
  // 编辑器未带 BuildTarget 时常见 WindowsEditor / WindowsPlayer
  if (/windows/i.test(ua) || /editor/i.test(ua)) return "StandaloneWindows64";
  return "StandaloneWindows64";
}

function cdnRootForFolder(folder) {
  return path.join(CDN_PARENT, folder);
}

function resolveCdnRoot(explicit) {
  if (explicit) return explicit;
  const platforms = listPlatformDirs();
  if (platforms.length === 1) return platforms[0].full;
  const win = platforms.find((p) => p.name === "StandaloneWindows64");
  if (win) return win.full;
  return platforms[0] ? platforms[0].full : "";
}

function tryLatestManifest(cdnRoot) {
  try {
    return { ok: true, ...latestManifest(cdnRoot) };
  } catch (err) {
    return { ok: false, error: err.message, root: cdnRoot };
  }
}

function guessRequestPlatform(req, body, url) {
  var fromBody = body && body.platform ? String(body.platform).trim() : "";
  if (fromBody) return fromBody;
  var fromQuery = url.searchParams.get("platform");
  if (fromQuery) return String(fromQuery).trim();
  var ua = String((req.headers && req.headers["user-agent"]) || "");
  if (/android/i.test(ua)) return "Android";
  if (/iphone|ipad|ios/i.test(ua)) return "iOS";
  return "WindowsPlayer";
}

function latestManifest(cdnRoot) {
  const dir = path.join(cdnRoot, "Manifests");
  if (!fs.existsSync(dir))
    throw new Error("未找到 Manifests 目录，请先 ZeonAsset / Publish CDN。\n" + dir);

  const files = fs
    .readdirSync(dir)
    .filter((name) => /^manifest_.*\.bytes$/i.test(name))
    .map((name) => {
      const full = path.join(dir, name);
      return { name, mtime: fs.statSync(full).mtimeMs };
    })
    .sort((a, b) => b.mtime - a.mtime);

  if (files.length === 0)
    throw new Error("Manifests 里没有 manifest_*.bytes，请先 Publish CDN。\n" + dir);

  let name = files[0].name;
  let digest = "";
  const match = name.match(MANIFEST_NAME_RE);
  if (match) digest = match[1].toLowerCase();

  const sidecar = path.join(dir, name.replace(/\.bytes$/i, ".json"));
  if (fs.existsSync(sidecar)) {
    try {
      const data = JSON.parse(fs.readFileSync(sidecar, "utf8"));
      if (data.ManifestHash) digest = String(data.ManifestHash).trim();
      if (data.ManifestFileName)
        name = String(data.ManifestFileName).trim() || name;
    } catch (_) {}
  }
  return { name, hash: digest };
}

function mimeOf(filePath) {
  const ext = path.extname(filePath).toLowerCase();
  if (ext === ".json") return "application/json; charset=utf-8";
  if (ext === ".txt") return "text/plain; charset=utf-8";
  return "application/octet-stream";
}

function sendJson(res, status, payload) {
  const raw = Buffer.from(JSON.stringify(payload, null, 2), "utf8");
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": raw.length,
    "Cache-Control": "no-store",
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
  });
  res.end(raw);
}

function sendFile(res, filePath) {
  const stat = fs.statSync(filePath);
  res.writeHead(200, {
    "Content-Type": mimeOf(filePath),
    "Content-Length": stat.size,
    "Cache-Control": "public, max-age=60",
    "Access-Control-Allow-Origin": "*",
  });
  fs.createReadStream(filePath).pipe(res);
}

function readBody(req) {
  return new Promise((resolve) => {
    const chunks = [];
    let total = 0;
    const maxBytes = 8 * 1024 * 1024;
    req.on("data", (chunk) => {
      total += chunk.length;
      if (total > maxBytes) {
        resolve({ raw: "", json: null, tooLarge: true });
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on("end", () => {
      const raw = Buffer.concat(chunks).toString("utf8");
      const trimmed = raw.trim();
      if (!trimmed) {
        resolve({ raw: "", json: {} });
        return;
      }
      try {
        const parsed = JSON.parse(trimmed);
        resolve({
          raw,
          json: parsed && typeof parsed === "object" ? parsed : {},
        });
      } catch (err) {
        resolve({ raw, json: null });
      }
    });
  });
}

function isVersionCheckPath(pathname) {
  return (
    pathname === "/version-check" ||
    pathname === "/version-check/" ||
    pathname === "/api/v1/version_check" ||
    pathname === "/version_check.json"
  );
}

function isDeviceLogPath(pathname) {
  return (
    pathname === "/device-log" ||
    pathname === "/device-log/" ||
    pathname === "/log" ||
    pathname === "/log/"
  );
}

function saveDeviceLog(body, rawText, meta) {
  const logDir = path.join(REPO_ROOT, "Logs", "device");
  fs.mkdirSync(logDir, { recursive: true });
  const stamp = new Date()
    .toISOString()
    .replace(/[:.]/g, "-")
    .replace("T", "_")
    .slice(0, 19);
  const fileName = "upload-" + stamp + ".log";
  const filePath = path.join(logDir, fileName);
  const latestPath = path.join(logDir, "latest.log");

  let text = "";
  if (body && typeof body.text === "string" && body.text.length > 0)
    text = body.text;
  else if (body && Array.isArray(body.lines))
    text = body.lines.join("\n");
  else if (typeof rawText === "string" && rawText.length > 0 && !String(rawText).trim().startsWith("{"))
    text = rawText;
  if (!text) text = "(empty log body)\n";

  const header = [
    "=== Zeon device log upload ===",
    "time=" + new Date().toISOString(),
    "from=" + (meta.ip || "?"),
    "device=" + ((body && body.device) || ""),
    "platform=" + ((body && body.platform) || ""),
    "app_version=" + ((body && body.app_version) || ""),
    "source=" + ((body && body.source) || ""),
    "===",
    "",
  ].join("\n");

  let content = header + String(text).replace(/\r\n/g, "\n");
  if (!content.endsWith("\n")) content += "\n";
  fs.writeFileSync(filePath, content, "utf8");
  fs.writeFileSync(latestPath, content, "utf8");
  return { filePath, fileName, bytes: Buffer.byteLength(content, "utf8") };
}

function safeCdnFile(cdnRoot, relUrlPath) {
  const rel = path.posix.normalize(relUrlPath.replace(/^\/+/, ""));
  if (!rel || rel === "." || rel.startsWith("..")) return null;
  const target = path.resolve(cdnRoot, ...rel.split("/"));
  const root = path.resolve(cdnRoot);
  if (target !== root && !target.startsWith(root + path.sep)) return null;
  return target;
}

function requestOrigin(req, fallbackOrigin) {
  const host = String((req && req.headers && req.headers.host) || "").trim();
  if (host) return "http://" + host;
  return fallbackOrigin;
}

function lanIpv4s() {
  const ips = [];
  const nics = os.networkInterfaces();
  Object.keys(nics).forEach((name) => {
    (nics[name] || []).forEach((n) => {
      if ((n.family === "IPv4" || n.family === 4) && !n.internal && String(n.address).indexOf("169.254.") !== 0)
        ips.push(n.address);
    });
  });
  return ips;
}

function parseAuthority(value, defaultPort) {
  let s = String(value || "").trim();
  if (!s) return { host: "", port: defaultPort };
  s = s.replace(/^https?:\/\//i, "");
  const slash = s.indexOf("/");
  if (slash >= 0) s = s.slice(0, slash);
  const colon = s.lastIndexOf(":");
  if (colon > 0 && /^\d+$/.test(s.slice(colon + 1))) {
    return { host: s.slice(0, colon), port: Number(s.slice(colon + 1)) };
  }
  return { host: s, port: defaultPort };
}

function loadDevEndpoints() {
  const file = path.join(__dirname, "local_dev.json");
  const defaults = {
    check_url: "",
    server_url: "",
    cdn_host: "",
    listenPort: 8080,
    publicHost: "",
    gameServerHost: "",
    gameServerPort: 8888,
  };
  try {
    if (!fs.existsSync(file)) return defaults;
    const raw = JSON.parse(fs.readFileSync(file, "utf8")) || {};
    const check = String(raw.check_url || "").trim();
    const server = String(raw.server_url || "").trim();
    const cdn = String(raw.cdn_host || "").trim();
    const oldHost = String(raw.lan_host || "").trim();
    const oldVc = Number(raw.version_check_port) || 0;
    const oldGame = Number(raw.game_server_port) || 0;
    const checkAuth = parseAuthority(check, oldVc || 8080);
    const serverAuth = parseAuthority(server, oldGame || 8888);
    const cdnAuth = parseAuthority(cdn, checkAuth.port);
    return {
      check_url: check,
      server_url: server,
      cdn_host: cdn.replace(/\/+$/, ""),
      listenPort: checkAuth.port || cdnAuth.port || oldVc || 8080,
      publicHost: checkAuth.host || cdnAuth.host || oldHost,
      gameServerHost: serverAuth.host || checkAuth.host || oldHost,
      gameServerPort: serverAuth.port || oldGame || 8888,
    };
  } catch (err) {
    console.warn("[local_dev] read failed:", err.message);
    return defaults;
  }
}

function pickLanIp(preferred) {
  const ips = lanIpv4s();
  const want = String(preferred || "").trim();
  if (want && ips.indexOf(want) >= 0) return want;
  if (want) {
    console.warn("[local_dev] preferred host not on this machine, fallback auto:", want);
  }
  const ranked = ips.slice().sort((a, b) => lanRank(a) - lanRank(b));
  return ranked[0] || "127.0.0.1";
}

function lanRank(ip) {
  if (String(ip).indexOf("192.168.") === 0) return 0;
  if (String(ip).indexOf("10.") === 0) return 1;
  return 2;
}

function clientIp(req) {
  return String((req.socket && req.socket.remoteAddress) || "")
    .replace(/^::ffff:/, "")
    .replace(/^::1$/, "127.0.0.1") || "?";
}

function formatBytes(n) {
  const size = Number(n) || 0;
  if (size < 1024) return size + "B";
  if (size < 1024 * 1024) return (size / 1024).toFixed(1) + "KB";
  return (size / (1024 * 1024)).toFixed(2) + "MB";
}

function accessLog(req, status, extra) {
  const time = new Date().toTimeString().slice(0, 8);
  const ip = clientIp(req);
  const ua = String((req.headers && req.headers["user-agent"]) || "");
  let who = ip;
  if (ip === "127.0.0.1" || lanIpv4s().indexOf(ip) >= 0) who = ip + "(本机)";
  else if (/android/i.test(ua)) who = ip + "(Android)";
  else if (/unity/i.test(ua)) who = ip + "(Unity)";
  const bits = [time, who, req.method, req.url, String(status)];
  if (extra) bits.push(extra);
  console.log(bits.join("  "));
}

function buildData(state, localHash, cdnHost, latest) {
  const hash = String(localHash || "").trim().toLowerCase();
  const manifestHash = latest && latest.hash ? String(latest.hash) : "";
  let hasUpdate = true;
  if (state.noUpdate) hasUpdate = false;
  else if (!state.forceUpdate && hash && manifestHash && hash === manifestHash.toLowerCase())
    hasUpdate = false;

  return {
    has_update: hasUpdate,
    status: "normal",
    cdn_host: cdnHost,
    manifest_name: hasUpdate && latest ? latest.name : "",
    manifest_hash: hasUpdate ? manifestHash : "",
    manifest_url: "",
    dispatch_url: (() => {
      try {
        return new URL(cdnHost).origin + "/version-check";
      } catch (_) {
        return "/version-check";
      }
    })(),
    game_server_host: state.gameServerHost,
    game_server_port: state.gameServerPort,
    log_upload_url: (() => {
      try {
        return new URL(cdnHost).origin + "/device-log";
      } catch (_) {
        return "/device-log";
      }
    })(),
    force_update: false,
    store_url: "",
    notice: {
      title: hasUpdate ? "发现更新" : "已是最新",
      content: "local-dev has_update=" + String(hasUpdate).toLowerCase(),
    },
  };
}

function main() {
  const args = parseArgs(process.argv);
  if (args.help) {
    console.log(`node local_dev_server.js [--port 8080] [--cdn <dir>] [--no-update]
  一个进程同时提供 VersionCheck 和 CDN 静态文件。
  可读 Tools/local_dev.json（check_url / server_url / cdn_host）。`);
    process.exit(0);
  }

  const dev = loadDevEndpoints();
  if (!args.portFromCli) args.port = dev.listenPort || args.port;
  const publicHost =
    args.host === "0.0.0.0" || args.host === "::"
      ? (dev.publicHost || pickLanIp(""))
      : args.host;
  const origin = `http://${publicHost}:${args.port}`;
  const state = {
    explicitCdn: args.cdn ? path.resolve(args.cdn) : "",
    noUpdate: args.noUpdate,
    forceUpdate: args.forceUpdate,
    cdnHost: dev.cdn_host,
    gameServerHost: dev.gameServerHost || publicHost,
    gameServerPort: dev.gameServerPort || 8888,
  };

  const server = http.createServer(async (req, res) => {
    const url = new URL(req.url || "/", origin);
    const pathname = decodeURIComponent(url.pathname);
    accessLog(req, "-", "recv");

    if (req.method === "OPTIONS") {
      res.writeHead(204, {
        "Access-Control-Allow-Origin": "*",
        "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
        "Access-Control-Allow-Headers": "Content-Type",
      });
      res.end();
      return;
    }

    if (isVersionCheckPath(pathname) && (req.method === "POST" || req.method === "GET")) {
      const packed = req.method === "POST" ? await readBody(req) : { json: {}, raw: "" };
      if (packed.tooLarge) {
        accessLog(req, 413, "body too large");
        sendJson(res, 413, { code: 413, message: "body too large" });
        return;
      }
      const body = packed.json || {};
      const hash =
        body.local_manifest_hash ||
        url.searchParams.get("local_manifest_hash") ||
        "";
      let folder = state.explicitCdn
        ? path.basename(state.explicitCdn)
        : folderForPlatform(guessRequestPlatform(req, body, url));
      let root = state.explicitCdn || cdnRootForFolder(folder);
      let latest = tryLatestManifest(root);
      if (!latest.ok && !state.explicitCdn) {
        const platforms = listPlatformDirs();
        for (let i = 0; i < platforms.length; i++) {
          const alt = tryLatestManifest(platforms[i].full);
          if (alt.ok) {
            console.log(
              "  fallback CDN folder:",
              folder,
              "->",
              platforms[i].name,
              "(" + latest.error + ")"
            );
            folder = platforms[i].name;
            root = platforms[i].full;
            latest = alt;
            break;
          }
        }
      }
      if (!latest.ok) {
        accessLog(req, 503, folder + "  " + latest.error);
        sendJson(res, 503, {
          code: 503,
          message:
            "CDN/" +
            folder +
            " 还没有内容。Unity 锁定 Windows 64 后目录名是 StandaloneWindows64。请 ZeonAsset / Build，再 Publish CDN。",
          platform_folder: folder,
          cdn_root: root,
        });
        return;
      }

      const reqOrigin = requestOrigin(req, origin);
      const cdnBase = state.cdnHost || reqOrigin;
      const cdnHost = state.explicitCdn ? reqOrigin : cdnBase + "/" + folder;
      const data = buildData(state, hash, cdnHost, latest);
      accessLog(
        req,
        200,
        folder +
          " has_update=" +
          data.has_update +
          " hash=" +
          (hash || "(empty)") +
          " -> " +
          latest.hash
      );
      sendJson(res, 200, { code: 200, data });
      return;
    }

    // POST /device-log  — 同进程 API：手机上传 zeon.log
    if (isDeviceLogPath(pathname) && req.method === "POST") {
      const packed = await readBody(req);
      if (packed.tooLarge) {
        accessLog(req, 413, "log too large");
        sendJson(res, 413, { code: 413, message: "log too large (max 8MB)" });
        return;
      }
      try {
        const saved = saveDeviceLog(packed.json, packed.raw, { ip: clientIp(req) });
        accessLog(req, 200, formatBytes(saved.bytes) + "  " + saved.fileName);
        console.log("  [device-log] saved", saved.filePath);
        sendJson(res, 200, {
          code: 200,
          data: {
            file: saved.fileName,
            path: saved.filePath,
            bytes: saved.bytes,
            latest: path.join(path.dirname(saved.filePath), "latest.log"),
          },
        });
      } catch (err) {
        accessLog(req, 500, err.message);
        sendJson(res, 500, { code: 500, message: err.message || "save failed" });
      }
      return;
    }

    if (req.method === "GET" && pathname === "/") {
      const reqOrigin = requestOrigin(req, origin);
      accessLog(req, 200);
      sendJson(res, 200, {
        api: reqOrigin + "/version_check.json",
        device_log: reqOrigin + "/device-log",
        platforms: listPlatformDirs().map((p) => p.name),
        cdn_parent: CDN_PARENT,
      });
      return;
    }

    if (req.method !== "GET") {
      accessLog(req, 405);
      sendJson(res, 405, { code: 405, message: "method not allowed" });
      return;
    }

    let rel = pathname;
    if (rel === "/cdn" || rel.startsWith("/cdn/"))
      rel = rel.slice("/cdn".length) || "/";
    rel = rel.replace(/^\/+/, "");

    let root = state.explicitCdn;
    let fileRel = rel;
    if (!root) {
      const slash = rel.indexOf("/");
      const first = slash >= 0 ? rel.slice(0, slash) : rel;
      const rest = slash >= 0 ? rel.slice(slash + 1) : "";
      const known = listPlatformDirs().some((p) => p.name === first);
      if (known) {
        root = cdnRootForFolder(first);
        fileRel = rest;
      } else {
        root = resolveCdnRoot("");
        fileRel = rel;
      }
    }

    const filePath = root ? safeCdnFile(root, fileRel) : null;
    if (!filePath || !fs.existsSync(filePath) || !fs.statSync(filePath).isFile()) {
      accessLog(req, 404);
      sendJson(res, 404, { code: 404, message: "file not found", path: pathname });
      return;
    }

    const size = fs.statSync(filePath).size;
    accessLog(req, 200, formatBytes(size) + "  " + path.basename(filePath));
    sendFile(res, filePath);
  });

  server.listen(args.port, args.host, () => {
    const checkUrl = dev.check_url || origin + "/version-check";
    const platforms = listPlatformDirs();
    console.log("");
    console.log("本地开发服务已启动（API + CDN 同一进程）");
    console.log("  地址              ", checkUrl, "(POST)");
    console.log("  兼容 GET          ", origin + "/version_check.json");
    console.log("  日志上传          ", origin + "/device-log", "(POST)");
    console.log("  游戏服下发        ", state.gameServerHost + ":" + state.gameServerPort);
    console.log("  磁盘根            ", CDN_PARENT);
    console.log("  配置              Tools/local_dev.json");
    if (platforms.length === 0) {
      console.log("  平台目录          （空）清过 CDN 或还没 Publish");
      console.log("  Windows 64 对应   CDN/StandaloneWindows64");
      console.log("  请 ZeonAsset / Build，再 Publish CDN。服务可以一直开着。");
    } else {
      platforms.forEach((p) => {
        const latest = tryLatestManifest(p.full);
        const hint = latest.ok ? latest.name + " " + latest.hash : "无清单";
        console.log("  平台              ", p.name, " ", hint);
      });
    }
    if (state.noUpdate) console.log("  模式              --no-update（永远 has_update=false）");
    console.log("");
    console.log("  boot_config.version_check_url =", checkUrl);
    console.log("  下面是访问日志（游戏点重试后这里应有新行）");
    console.log("");
  });
}

main();
