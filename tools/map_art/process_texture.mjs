#!/usr/bin/env node
// Deterministic local repair for versioned diagnostic assets. Never overwrites an existing output.
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import crypto from 'node:crypto';

const signature = Buffer.from('89504e470d0a1a0a', 'hex');
const crcTable = new Uint32Array(256);
for (let n = 0; n < 256; n++) {
  let c = n;
  for (let k = 0; k < 8; k++) c = (c & 1) ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  crcTable[n] = c >>> 0;
}

function crc32(bytes) {
  let c = 0xffffffff;
  for (const byte of bytes) c = crcTable[(c ^ byte) & 255] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const label = Buffer.from(type, 'ascii');
  const out = Buffer.alloc(data.length + 12);
  out.writeUInt32BE(data.length, 0); label.copy(out, 4); data.copy(out, 8);
  out.writeUInt32BE(crc32(Buffer.concat([label, data])), data.length + 8);
  return out;
}

export function decodePng(filePath) {
  const file = fs.readFileSync(filePath);
  if (!file.subarray(0, 8).equals(signature)) throw new Error(`Not a PNG: ${filePath}`);
  let header; const compressed = [];
  for (let offset = 8; offset < file.length;) {
    const length = file.readUInt32BE(offset), type = file.toString('ascii', offset + 4, offset + 8);
    const data = file.subarray(offset + 8, offset + 8 + length);
    if (type === 'IHDR') header = data;
    if (type === 'IDAT') compressed.push(data);
    offset += length + 12;
  }
  if (!header) throw new Error(`Missing IHDR: ${filePath}`);
  const width = header.readUInt32BE(0), height = header.readUInt32BE(4);
  const bitDepth = header[8], colorType = header[9], interlace = header[12];
  if (bitDepth !== 8 || interlace !== 0 || ![2, 6].includes(colorType))
    throw new Error('Only non-interlaced RGB/RGBA 8-bit PNGs are supported.');
  const channels = colorType === 6 ? 4 : 3, stride = width * channels;
  const raw = zlib.inflateSync(Buffer.concat(compressed));
  if (raw.length !== height * (stride + 1)) throw new Error('Unexpected decompressed PNG size.');
  const pixels = Buffer.alloc(height * stride);
  const paeth = (a, b, c) => {
    const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
    return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
  };
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    if (filter > 4) throw new Error(`Unsupported PNG filter ${filter}.`);
    for (let x = 0; x < stride; x++) {
      const a = x >= channels ? pixels[y * stride + x - channels] : 0;
      const b = y ? pixels[(y - 1) * stride + x] : 0;
      const c = y && x >= channels ? pixels[(y - 1) * stride + x - channels] : 0;
      const predict = filter === 0 ? 0 : filter === 1 ? a : filter === 2 ? b : filter === 3 ? Math.floor((a + b) / 2) : paeth(a, b, c);
      pixels[y * stride + x] = (raw[y * (stride + 1) + 1 + x] + predict) & 255;
    }
  }
  return { width, height, channels, pixels, bytes: file.length, sha256: sha(file) };
}

export function encodePng(image) {
  const { width, height, channels, pixels } = image;
  if (![3, 4].includes(channels) || pixels.length !== width * height * channels) throw new Error('Invalid image buffer.');
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0); header.writeUInt32BE(height, 4);
  header[8] = 8; header[9] = channels === 4 ? 6 : 2;
  const stride = width * channels, raw = Buffer.alloc(height * (stride + 1));
  for (let y = 0; y < height; y++) pixels.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
  return Buffer.concat([signature, chunk('IHDR', header), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

const clampByte = value => Math.max(0, Math.min(255, Math.round(value)));
const smoothstep = t => { t = Math.max(0, Math.min(1, t)); return t * t * (3 - 2 * t); };

function sampleLinear(image, x, y, channel) {
  const x0 = Math.floor(x), y0 = Math.floor(y), x1 = Math.min(image.width - 1, x0 + 1), y1 = Math.min(image.height - 1, y0 + 1);
  const tx = x - x0, ty = y - y0, c = image.channels, p = image.pixels;
  const at = (px, py) => p[(py * image.width + px) * c + channel];
  const a = at(x0, y0) * (1 - tx) + at(x1, y0) * tx;
  const b = at(x0, y1) * (1 - tx) + at(x1, y1) * tx;
  return a * (1 - ty) + b * ty;
}

function seamlessAxis(source, horizontal, feather) {
  const out = Buffer.alloc(source.pixels.length), { width, height, channels } = source;
  const max = (horizontal ? width : height) - 1;
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const u = (horizontal ? x : y) / max;
    const edgeDistance = Math.min(u, 1 - u);
    const originalWeight = smoothstep(edgeDistance / feather);
    const shifted = u < 0.5 ? u + 0.5 : u - 0.5;
    const sx = horizontal ? shifted * (width - 1) : x;
    const sy = horizontal ? y : shifted * (height - 1);
    const offset = (y * width + x) * channels;
    for (let c = 0; c < channels; c++) {
      const original = source.pixels[offset + c], alternate = sampleLinear(source, sx, sy, c);
      out[offset + c] = clampByte(original * originalWeight + alternate * (1 - originalWeight));
    }
  }
  return { ...source, pixels: out };
}

export function makeSeamless(source, feather = 0.12) {
  if (!(feather > 0 && feather < 0.25)) throw new Error('Feather must be between 0 and 0.25.');
  return seamlessAxis(seamlessAxis(source, true, feather), false, feather);
}

export function extractCheckerAlpha(source, transparentAt = 0.045, opaqueAt = 0.55) {
  if (source.channels !== 3) throw new Error('Checker extraction expects an RGB source.');
  const out = Buffer.alloc(source.width * source.height * 4);
  let transparent = 0, partial = 0, opaque = 0;
  for (let i = 0, q = 0; i < source.pixels.length; i += 3, q += 4) {
    const r = source.pixels[i], g = source.pixels[i + 1], b = source.pixels[i + 2];
    const rawInk = (255 - Math.min(r, g, b)) / 255;
    const alpha = smoothstep((rawInk - transparentAt) / (opaqueAt - transparentAt));
    const a = clampByte(alpha * 255);
    if (a === 0) transparent++; else if (a === 255) opaque++; else partial++;
    if (a === 0) { out[q] = out[q + 1] = out[q + 2] = 0; }
    else {
      // Remove the baked near-white matte and keep straight-alpha ink color.
      const base = 255 * (1 - alpha), divisor = Math.max(alpha, 1 / 255);
      out[q] = clampByte((r - base) / divisor);
      out[q + 1] = clampByte((g - base) / divisor);
      out[q + 2] = clampByte((b - base) / divisor);
    }
    out[q + 3] = a;
  }
  return { image: { width: source.width, height: source.height, channels: 4, pixels: out }, alpha: { transparent, partial, opaque, total: source.width * source.height } };
}

export function decontaminateTransparentEdges(source, seedAlpha = 220, edgeRgbCap = 72, alphaCutoff = 5) {
  if (source.channels !== 4) throw new Error('Edge decontamination expects RGBA input.');
  if (!(edgeRgbCap > 0 && edgeRgbCap <= 255)) throw new Error('Edge RGB cap must be between 1 and 255.');
  if (!(alphaCutoff >= 0 && alphaCutoff < seedAlpha)) throw new Error('Alpha cutoff must be non-negative and below seed alpha.');
  const count = source.width * source.height, nearest = new Int32Array(count), queue = new Int32Array(count);
  nearest.fill(-1); let head = 0, tail = 0;
  for (let i = 0; i < count; i++) if (source.pixels[i * 4 + 3] >= seedAlpha) { nearest[i] = i; queue[tail++] = i; }
  if (!tail) throw new Error('No sufficiently opaque pixels for edge decontamination.');
  while (head < tail) {
    const current = queue[head++], x = current % source.width, y = Math.floor(current / source.width), seed = nearest[current];
    const visit = next => { if (nearest[next] < 0) { nearest[next] = seed; queue[tail++] = next; } };
    if (x) visit(current - 1); if (x + 1 < source.width) visit(current + 1);
    if (y) visit(current - source.width); if (y + 1 < source.height) visit(current + source.width);
  }
  const out = Buffer.from(source.pixels);
  for (let i = 0; i < count; i++) {
    const alphaByte = out[i * 4 + 3];
    if (alphaByte <= alphaCutoff) {
      out[i * 4] = out[i * 4 + 1] = out[i * 4 + 2] = out[i * 4 + 3] = 0;
      continue;
    }
    const alpha = alphaByte / 255;
    if (alpha <= 0 || alpha >= 1) continue;
    const seed = nearest[i], seedOffset = seed * 4;
    const seedMax = Math.max(source.pixels[seedOffset], source.pixels[seedOffset + 1], source.pixels[seedOffset + 2]);
    const capScale = seedMax > edgeRgbCap ? edgeRgbCap / seedMax : 1;
    // A baked pale matte is most visible on low-alpha pixels. Pull those pixels
    // toward a capped nearby ink colour, while leaving opaque interior washes intact.
    const blend = 0.95 * Math.pow(1 - alpha, 0.55);
    for (let c = 0; c < 3; c++) {
      const inkEdge = source.pixels[seedOffset + c] * capScale;
      out[i * 4 + c] = clampByte(out[i * 4 + c] * (1 - blend) + inkEdge * blend);
    }
  }
  return { ...source, pixels: out };
}

export function tile3x3(source) {
  const width = source.width * 3, height = source.height * 3, out = Buffer.alloc(width * height * source.channels);
  const row = source.width * source.channels, targetRow = width * source.channels;
  for (let ty = 0; ty < 3; ty++) for (let y = 0; y < source.height; y++) for (let tx = 0; tx < 3; tx++)
    source.pixels.copy(out, (ty * source.height + y) * targetRow + tx * row, y * row, (y + 1) * row);
  return { width, height, channels: source.channels, pixels: out };
}

export function composite(source, hex) {
  if (source.channels !== 4 || !/^[0-9a-fA-F]{6}$/.test(hex)) throw new Error('Preview requires RGBA input and a six-digit RGB color.');
  const background = [0, 2, 4].map(i => parseInt(hex.slice(i, i + 2), 16));
  const out = Buffer.alloc(source.width * source.height * 3);
  for (let i = 0, q = 0; i < source.pixels.length; i += 4, q += 3) {
    const alpha = source.pixels[i + 3] / 255;
    for (let c = 0; c < 3; c++) out[q + c] = clampByte(source.pixels[i + c] * alpha + background[c] * (1 - alpha));
  }
  return { width: source.width, height: source.height, channels: 3, pixels: out };
}

function sha(bytes) { return crypto.createHash('sha256').update(bytes).digest('hex'); }
function writeNew(output, image) {
  if (fs.existsSync(output)) throw new Error(`Refusing to overwrite existing output: ${output}`);
  fs.mkdirSync(path.dirname(output), { recursive: true });
  const bytes = encodePng(image); fs.writeFileSync(output, bytes);
  return { path: path.resolve(output), bytes: bytes.length, sha256: sha(bytes), width: image.width, height: image.height, channels: image.channels };
}

function parseArgs(argv) {
  const [mode, ...rest] = argv, options = {};
  for (let i = 0; i < rest.length; i += 2) {
    if (!rest[i]?.startsWith('--') || rest[i + 1] == null) throw new Error(`Invalid argument near ${rest[i] ?? '<end>'}.`);
    options[rest[i].slice(2)] = rest[i + 1];
  }
  return { mode, options };
}

if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(new URL(import.meta.url).pathname)) {
  try {
    const { mode, options } = parseArgs(process.argv.slice(2));
    if (!options.input || !options.output) throw new Error('Usage: process_texture.mjs <seamless|alpha|tile|preview> --input FILE --output FILE [options]');
    const source = decodePng(options.input); let result, extra = {};
    if (mode === 'seamless') {
      const feather = Number(options.feather ?? 0.12); result = makeSeamless(source, feather); extra.feather = feather;
    } else if (mode === 'alpha') {
      const extracted = extractCheckerAlpha(source, Number(options.transparentAt ?? 0.045), Number(options.opaqueAt ?? 0.55));
      const seedAlpha = Number(options.seedAlpha ?? 220), edgeRgbCap = Number(options.edgeRgbCap ?? 72), alphaCutoff = Number(options.alphaCutoff ?? 5);
      result = options.decontaminate === '1' ? decontaminateTransparentEdges(extracted.image, seedAlpha, edgeRgbCap, alphaCutoff) : extracted.image;
      extra.alpha = extracted.alpha; extra.decontaminated = options.decontaminate === '1';
      if (extra.decontaminated) Object.assign(extra, { seedAlpha, edgeRgbCap, alphaCutoff });
    } else if (mode === 'tile') result = tile3x3(source);
    else if (mode === 'preview') result = composite(source, options.background ?? 'ffffff');
    else throw new Error(`Unknown mode: ${mode}`);
    const output = writeNew(options.output, result);
    console.log(JSON.stringify({ mode, input: { path: path.resolve(options.input), bytes: source.bytes, sha256: source.sha256, width: source.width, height: source.height, channels: source.channels }, output, ...extra }, null, 2));
  } catch (error) { console.error(error.stack || error.message); process.exitCode = 1; }
}
