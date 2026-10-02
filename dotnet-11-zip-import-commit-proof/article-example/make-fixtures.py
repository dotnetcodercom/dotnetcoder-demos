from pathlib import Path
from zipfile import ZipFile, ZIP_STORED
import struct

with ZipFile("valid.zip", "w", compression=ZIP_STORED) as archive:
    archive.writestr("first.txt", "first=alpha")
    archive.writestr("second.txt", "second=bravo")

with ZipFile("valid.zip") as archive:
    entry = archive.getinfo("second.txt")
    data = bytearray(Path("valid.zip").read_bytes())
    name_size, extra_size = struct.unpack_from("<HH", data, entry.header_offset + 26)
    payload = entry.header_offset + 30 + name_size + extra_size
    data[payload + entry.file_size - 1] ^= 1
    Path("corrupt-last.zip").write_bytes(data)

Path("records.json").write_text('{"seed":"keep"}', encoding="utf-8")
print("Created valid.zip, corrupt-last.zip and records.json")
