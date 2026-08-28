import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join } from "node:path";

const refRoot = process.env.DECIMEN_REF_ROOT;
const fixtureRoot = process.env.DECIMEN_FIXTURE_ROOT;
const mode = process.env.DECIMEN_MODE ?? "generate";
if (!refRoot || !fixtureRoot) throw new Error("DECIMEN_REF_ROOT and DECIMEN_FIXTURE_ROOT are required");

const protocol = await import(new URL(`file://${join(refRoot, "shared/protocol.ts")}`).href);
const fountain = await import(new URL(`file://${join(refRoot, "shared/fountain.ts")}`).href);
mkdirSync(fixtureRoot, { recursive: true });

if (mode === "generate") {
  const source = new Uint8Array([0, 1, 2, 127, 128, 254, 255, 0x44, 0x43, 0x46, 0x32]);
  const packed = await protocol.packFile("interop/test.bin", "application/octet-stream", source);
  const framePayload = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8]);
  const payloadFnv = protocol.fnv1a(packed.container);
  const frame = protocol.packFrame({ sessionId: 0xBEEF, seq: 0x01020304, k: 3, blockLen: framePayload.length, totalLen: packed.container.length, payloadFnv, flags: 0 }, framePayload);
  const encoder = new fountain.LTEncoder(packed.container, 8, 0xBEEF);
  const fountainFrames = Array.from({ length: encoder.k * 2 }, (_, seq) => Buffer.from(protocol.packFrame({ sessionId: 0xBEEF, seq, k: encoder.k, blockLen: 8, totalLen: packed.container.length, payloadFnv, flags: 0 }, encoder.encode(seq))).toString("base64"));
  writeFileSync(join(fixtureRoot, "ts-to-cs.json"), JSON.stringify({
    source: Buffer.from(source).toString("base64"), dcf2: Buffer.from(packed.container).toString("base64"), frame: Buffer.from(frame).toString("base64"),
    fountain: { blockLength: 8, sessionId: 0xBEEF, totalLength: packed.container.length, k: encoder.k, payloadFnv, frames: fountainFrames },
  }, null, 2));
  console.log("TypeScript fixtures generated.");
  process.exit(0);
}

if (mode !== "verify") throw new Error(`Unknown DECIMEN_MODE: ${mode}`);
const cs = JSON.parse(readFileSync(join(fixtureRoot, "cs-to-ts.json"), "utf8"));
const source = Buffer.from(cs.source, "base64");
const unpacked = await protocol.unpackFile(Buffer.from(cs.dcf2, "base64"));
if (Buffer.compare(Buffer.from(unpacked.bytes), source) !== 0) throw new Error("TypeScript failed to recover C# DCF2 source bytes");
if (!(await protocol.verifyFile(unpacked))) throw new Error("TypeScript SHA-256 verification failed for C# DCF2");
for (const [name, encoded] of Object.entries(cs.frames)) {
  if (!protocol.parseFrame(new Uint8Array(Buffer.from(encoded, "base64")))) throw new Error(`TypeScript rejected C# frame ${name}`);
}
const decoder = new fountain.LTDecoder(cs.fountain.k, cs.fountain.blockLength, cs.fountain.sessionId, cs.fountain.totalLength);
for (const encoded of cs.fountain.frames) {
  const parsed = protocol.parseFrame(new Uint8Array(Buffer.from(encoded, "base64")));
  if (!parsed) throw new Error("TypeScript rejected a C# fountain frame");
  decoder.addFrame(parsed.header.seq, parsed.block);
}
const recovered = decoder.assemble();
if (!recovered) throw new Error("TypeScript fountain decoder did not complete on C# frames");
if (protocol.fnv1a(recovered) !== cs.fountain.payloadFnv) throw new Error("TypeScript fountain FNV-1a mismatch");
const recoveredFile = await protocol.unpackFile(recovered);
if (!(await protocol.verifyFile(recoveredFile))) throw new Error("TypeScript recovered file SHA-256 mismatch");
if (Buffer.compare(Buffer.from(recoveredFile.bytes), source) !== 0) throw new Error("TypeScript fountain recovery bytes differ from source");
console.log("Cross-platform TypeScript verification passed.");
