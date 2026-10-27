#!/usr/bin/env node
/* ------------------------------------------------------------------
   Local web server for the ID Maker
   ------------------------------------------------------------------
     npm start                 serve this folder on port 8000
     npm start -- --port 9000  use another port
     npm start -- --lan        also allow other computers on the office
                               network to open it

   "/" serves the built "Alpha Premier ID Maker.html"; "/src/" serves the
   development page. No dependencies — plain Node, works offline.
   ------------------------------------------------------------------ */
import { createServer } from 'node:http';
import { createReadStream, statSync } from 'node:fs';
import { join, extname, normalize, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { networkInterfaces } from 'node:os';

const root = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const flag = (name) => args.includes(`--${name}`);
const value = (name, fallback) => {
  const i = args.indexOf(`--${name}`);
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback;
};

const port = Number(value('port', process.env.PORT || 8000));
const lan = flag('lan');
const host = lan ? '0.0.0.0' : '127.0.0.1';
const APP = 'Alpha Premier ID Maker.html';

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.csv': 'text/csv; charset=utf-8',
  '.md': 'text/markdown; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
  '.pdf': 'application/pdf',
};

const server = createServer((req, res) => {
  let path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path === '/' || path === '') path = `/${APP}`;
  if (path.endsWith('/')) path += 'index.html';

  // keep every request inside this folder
  const target = join(root, normalize(path).replace(/^(\.\.[/\\])+/, ''));
  if (!target.startsWith(root)) { res.writeHead(403).end('Forbidden'); return; }

  let stat;
  try { stat = statSync(target); } catch { res.writeHead(404).end('Not found'); return; }
  if (stat.isDirectory()) { res.writeHead(301, { Location: `${path}/` }).end(); return; }

  res.writeHead(200, {
    'Content-Type': TYPES[extname(target).toLowerCase()] ?? 'application/octet-stream',
    'Content-Length': stat.size,
    'Cache-Control': 'no-store',
  });
  createReadStream(target).pipe(res);
});

server.on('error', (err) => {
  if (err.code === 'EADDRINUSE') {
    console.error(`Port ${port} is already in use. Try:  npm start -- --port ${port + 1}`);
    process.exit(1);
  }
  throw err;
});

server.listen(port, host, () => {
  console.log(`\n  Alpha Premier ID Maker\n`);
  console.log(`  On this computer:  http://localhost:${port}/`);
  if (lan) {
    for (const list of Object.values(networkInterfaces())) {
      for (const net of list ?? []) {
        if (net.family === 'IPv4' && !net.internal) console.log(`  On the network:    http://${net.address}:${port}/`);
      }
    }
    console.log(`\n  Windows may ask to allow Node through the firewall — say yes for private networks.`);
  } else {
    console.log(`  (other computers cannot reach this; use  npm start -- --lan  for that)`);
  }
  console.log(`  Development page:  http://localhost:${port}/src/\n`);
  console.log(`  Press Ctrl+C to stop.\n`);
});
