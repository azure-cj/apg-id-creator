/* ------------------------------------------------------------------
   A very small ZIP writer.
   ------------------------------------------------------------------
   `zip` is a Unix command and this gets run on Windows, where it is
   not there; PowerShell's Compress-Archive is, but shelling out to a
   different tool per platform is two things to go wrong instead of
   one. Node can already deflate, and the rest of a ZIP file is a few
   headers, so it writes its own — the same reasoning that gave this
   project a hand-written PDF writer.

   Deflate, no encryption, no zip64. Good to 4 GB and 65,535 files,
   which is comfortably more than five.
   ------------------------------------------------------------------ */
import { deflateRawSync } from 'node:zlib';

const CRC_TABLE = (() => {
  const t = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c;
  }
  return t;
})();

function crc32(buf) {
  let c = -1;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ -1) >>> 0;
}

/** MS-DOS packed date and time, which is what a ZIP header carries. */
function dosStamp(d = new Date()) {
  const time = (d.getHours() << 11) | (d.getMinutes() << 5) | (d.getSeconds() >> 1);
  const date = ((Math.max(1980, d.getFullYear()) - 1980) << 9) | ((d.getMonth() + 1) << 5) | d.getDate();
  return { time, date };
}

/**
 * entries: [{ name: 'folder/file.txt', data: Buffer }]
 * Returns the whole archive as a Buffer.
 */
export function zipSync(entries, when = new Date()) {
  const { time, date } = dosStamp(when);
  const locals = [];
  const central = [];
  let offset = 0;

  for (const { name, data } of entries) {
    const nameBuf = Buffer.from(name, 'utf8');
    const body = deflateRawSync(data, { level: 9 });
    const crc = crc32(data);

    const local = Buffer.alloc(30 + nameBuf.length);
    local.writeUInt32LE(0x04034b50, 0);       // local file header
    local.writeUInt16LE(20, 4);               // version needed
    local.writeUInt16LE(0x0800, 6);           // flags: UTF-8 names
    local.writeUInt16LE(8, 8);                // method: deflate
    local.writeUInt16LE(time, 10);
    local.writeUInt16LE(date, 12);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(body.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBuf.length, 26);
    nameBuf.copy(local, 30);
    locals.push(local, body);

    const dir = Buffer.alloc(46 + nameBuf.length);
    dir.writeUInt32LE(0x02014b50, 0);         // central directory entry
    dir.writeUInt16LE(20, 4);                 // version made by
    dir.writeUInt16LE(20, 6);                 // version needed
    dir.writeUInt16LE(0x0800, 8);
    dir.writeUInt16LE(8, 10);
    dir.writeUInt16LE(time, 12);
    dir.writeUInt16LE(date, 14);
    dir.writeUInt32LE(crc, 16);
    dir.writeUInt32LE(body.length, 20);
    dir.writeUInt32LE(data.length, 24);
    dir.writeUInt16LE(nameBuf.length, 28);
    dir.writeUInt32LE(offset, 42);            // where its local header is
    nameBuf.copy(dir, 46);
    central.push(dir);

    offset += local.length + body.length;
  }

  const dirBuf = Buffer.concat(central);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);           // end of central directory
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(dirBuf.length, 12);
  end.writeUInt32LE(offset, 16);

  return Buffer.concat([...locals, dirBuf, end]);
}
