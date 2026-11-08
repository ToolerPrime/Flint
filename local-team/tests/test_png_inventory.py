import importlib.util
from pathlib import Path
import struct
import unittest
import zlib

MODULE = Path(__file__).resolve().parents[1] / "tools" / "png_inventory.py"
spec = importlib.util.spec_from_file_location("png_inventory", MODULE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

SIG = b"\x89PNG\r\n\x1a\n"
def chunk(kind, payload=b""):
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind+payload))

IHDR = chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 6, 0, 0, 0))
END = chunk(b"IEND")

class InventoryTests(unittest.TestCase):
    def test_private_chunk_and_offsets(self):
        rows = module.inspect_png(SIG+IHDR+chunk(b"prVW", b"abc")+END)
        self.assertEqual([(r["type"], r["offset"], r["length"], r["private"]) for r in rows], [("IHDR",8,13,False),("prVW",33,3,True),("IEND",48,0,False)])
        self.assertTrue(all(r["crc_ok"] for r in rows))

    def test_rejects_malformed_containers(self):
        bad = [b"", SIG+END, SIG+IHDR, SIG+IHDR+END+b"x", SIG+IHDR+END+END, SIG+IHDR+IHDR+END,
               SIG+IHDR+chunk(b"IEND",b"x"), SIG+chunk(b"IHDR",b"x")+END,
               SIG+IHDR+chunk(b"abca")+END, SIG+IHDR+chunk(b"1ABC")+END,
               SIG+IHDR+struct.pack(">I",0xffffffff)+b"IDAT"+END]
        corrupted=bytearray(SIG+IHDR+END); corrupted[20]^=1; bad.append(bytes(corrupted))
        good=SIG+IHDR+END
        bad.extend(good[:n] for n in range(len(good)))
        for data in bad:
            with self.subTest(data=data):
                with self.assertRaises(ValueError): module.inspect_png(data)

if __name__ == "__main__": unittest.main()
