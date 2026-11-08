"""Read-only PNG container inventory; does not decode pixels or private data."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zlib


def inspect_png(data: bytes) -> list[dict]:
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("Invalid PNG signature")
    chunks = []
    offset = 8
    while offset < len(data):
        if len(data) - offset < 12:
            raise ValueError("Truncated chunk header or CRC")
        length = struct.unpack_from(">I", data, offset)[0]
        kind = data[offset+4:offset+8]
        end = offset + 12 + length
        if end > len(data):
            raise ValueError("Chunk exceeds file bounds")
        if not all(65 <= b <= 90 or 97 <= b <= 122 for b in kind) or kind[2] & 32:
            raise ValueError("Invalid chunk type or reserved bit")
        if not chunks and (kind != b"IHDR" or length != 13):
            raise ValueError("First chunk must be 13-byte IHDR")
        if chunks and kind == b"IHDR":
            raise ValueError("Duplicate IHDR")
        crc = struct.unpack_from(">I", data, end-4)[0]
        if zlib.crc32(memoryview(data)[offset+4:end-4]) != crc:
            raise ValueError("Chunk CRC mismatch")
        chunks.append({"offset": offset, "type": kind.decode("ascii"), "length": length,
                       "private": bool(kind[1] & 32), "crc_ok": True})
        offset = end
        if kind == b"IEND":
            if length != 0 or offset != len(data):
                raise ValueError("Nonempty IEND or trailing bytes")
            return chunks
    raise ValueError("Missing IEND")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="+")
    args = parser.parse_args()
    results = []
    failed = False
    for name in args.paths:
        result = {"path": name}
        try:
            data = Path(name).read_bytes()
            result["sha256"] = hashlib.sha256(data).hexdigest()
            result["chunks"] = inspect_png(data)
        except (OSError, ValueError) as exc:
            result["error"] = str(exc)
            failed = True
        results.append(result)
    print(json.dumps(results, indent=2))
    return int(failed)


if __name__ == "__main__":
    raise SystemExit(main())
