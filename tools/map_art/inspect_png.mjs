// Read-only PNG inspection for native-source and Unity-capture QA. No pixel writes, resizing, or repairs.
import fs from 'node:fs';
import zlib from 'node:zlib';
import crypto from 'node:crypto';

export function inspectPng(path) {
  const file = fs.readFileSync(path);
  if (file.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a') throw new Error(`Not PNG: ${path}`);
  let header; const parts = [];
  for (let offset = 8; offset < file.length;) {
    const length = file.readUInt32BE(offset), type = file.toString('ascii', offset + 4, offset + 8);
    const data = file.subarray(offset + 8, offset + 8 + length);
    if (type === 'IHDR') header = data;
    if (type === 'IDAT') parts.push(data);
    offset += length + 12;
  }
  const width = header.readUInt32BE(0), height = header.readUInt32BE(4), colorType = header[9];
  if (header[8] !== 8 || header[12] !== 0 || ![2, 6].includes(colorType)) throw new Error('Only non-interlaced RGB/RGBA 8-bit PNG inspection is supported.');
  const channels = colorType === 6 ? 4 : 3, stride = width * channels;
  const raw = zlib.inflateSync(Buffer.concat(parts)), decoded = Buffer.alloc(height * stride);
  function paeth(a, b, c) {
    const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
    return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
  }
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    if (filter > 4) throw new Error('Invalid PNG row filter');
    for (let x = 0; x < stride; x++) {
      const a = x >= channels ? decoded[y * stride + x - channels] : 0;
      const b = y > 0 ? decoded[(y - 1) * stride + x] : 0;
      const c = y > 0 && x >= channels ? decoded[(y - 1) * stride + x - channels] : 0;
      const predictor = filter === 0 ? 0 : filter === 1 ? a : filter === 2 ? b : filter === 3 ? Math.floor((a + b) / 2) : paeth(a, b, c);
      decoded[y * stride + x] = (raw[y * (stride + 1) + 1 + x] + predictor) & 255;
    }
  }
  const histogram = Array(256).fill(0); let transparent = 0, partial = 0;
  for (let i = 0; i < decoded.length; i += channels) {
    histogram[Math.round(decoded[i] * .2126 + decoded[i + 1] * .7152 + decoded[i + 2] * .0722)]++;
    if (channels === 4 && decoded[i + 3] === 0) transparent++;
    else if (channels === 4 && decoded[i + 3] < 255) partial++;
  }
  function percentile(f) { let count = 0; for (let i = 0; i < 256; i++) { count += histogram[i]; if (count >= width * height * f) return i; } return 255; }
  const pixel = (x, y) => [...decoded.subarray(y * stride + x * channels, y * stride + x * channels + channels)];
  return { path, width, height, colorType, bytes: file.length, sha256: crypto.createHash('sha256').update(file).digest('hex'),
    alpha: { channel: channels === 4, transparentPixels: transparent, partialPixels: partial },
    luminance: { p05: percentile(.05), p25: percentile(.25), p50: percentile(.5), p75: percentile(.75), p95: percentile(.95) },
    samples: { topLeft: pixel(5, 5), topCenter: pixel(width >> 1, 5), center: pixel(width >> 1, height >> 1),
      leftCenter: pixel(5, height >> 1), lowerLeft: pixel(width >> 2, Math.floor(height * .75)) } };
}
if (process.argv[1]?.endsWith('inspect_png.mjs')) console.log(JSON.stringify(process.argv.slice(2).map(inspectPng), null, 2));
