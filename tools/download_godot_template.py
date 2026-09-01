"""Download selected files from an official Godot export-template archive.

Godot 4.7's template manager downloads individual ZIP members with HTTP range
requests. This helper mirrors that behaviour so release builds can install only
the Windows template they require instead of downloading the entire 1.2 GB TPZ.
"""

from __future__ import annotations

import argparse
import binascii
import os
from pathlib import Path
import struct
import sys
import urllib.request
import zlib


EOCD_SIGNATURE = b"PK\x05\x06"
CENTRAL_SIGNATURE = b"PK\x01\x02"
LOCAL_SIGNATURE = b"PK\x03\x04"
TAIL_SIZE = 0x10000
CHUNK_SIZE = 1024 * 1024
USER_AGENT = "PROJECT-MIRROR-Godot-Exporter/1.0"


def request(
    url: str,
    *,
    method: str = "GET",
    byte_range: tuple[int, int] | None = None,
):
    """Open an official archive request and require range semantics when used."""

    headers = {"User-Agent": USER_AGENT, "Accept-Encoding": "identity"}
    if byte_range is not None:
        start, end = byte_range
        headers["Range"] = f"bytes={start}-{end}"

    response = urllib.request.urlopen(
        urllib.request.Request(url, headers=headers, method=method), timeout=120
    )
    if byte_range is not None and response.status != 206:
        response.close()
        raise RuntimeError(
            f"Server ignored HTTP range {byte_range}; refusing a full archive download."
        )
    return response


def archive_size(url: str) -> int:
    """Resolve redirects and read the remote archive's exact byte size."""

    with request(url, method="HEAD") as response:
        value = response.headers.get("Content-Length")
        if value is None:
            raise RuntimeError("Archive response did not include Content-Length.")
        return int(value)


def fetch_range(url: str, start: int, end: int) -> bytes:
    """Fetch one inclusive byte range."""

    with request(url, byte_range=(start, end)) as response:
        return response.read()


def find_member(url: str, member_name: str) -> dict[str, int]:
    """Read the ZIP central directory and return metadata for one member."""

    size = archive_size(url)
    tail_start = max(0, size - TAIL_SIZE)
    tail = fetch_range(url, tail_start, size - 1)
    eocd_offset = tail.rfind(EOCD_SIGNATURE)
    if eocd_offset < 0:
        raise RuntimeError("Could not find the ZIP end-of-central-directory record.")

    total_entries = struct.unpack_from("<H", tail, eocd_offset + 10)[0]
    directory_size = struct.unpack_from("<I", tail, eocd_offset + 12)[0]
    directory_start = struct.unpack_from("<I", tail, eocd_offset + 16)[0]

    if directory_start >= tail_start:
        directory = tail[
            directory_start - tail_start : directory_start - tail_start + directory_size
        ]
    else:
        directory = fetch_range(
            url, directory_start, directory_start + directory_size - 1
        )

    offset = 0
    for _ in range(total_entries):
        if directory[offset : offset + 4] != CENTRAL_SIGNATURE:
            raise RuntimeError("ZIP central directory is malformed.")

        compression = struct.unpack_from("<H", directory, offset + 10)[0]
        crc32 = struct.unpack_from("<I", directory, offset + 16)[0]
        compressed_size = struct.unpack_from("<I", directory, offset + 20)[0]
        uncompressed_size = struct.unpack_from("<I", directory, offset + 24)[0]
        name_length, extra_length, comment_length = struct.unpack_from(
            "<HHH", directory, offset + 28
        )
        local_offset = struct.unpack_from("<I", directory, offset + 42)[0]
        record_size = 46 + name_length + extra_length + comment_length
        name_bytes = directory[offset + 46 : offset + 46 + name_length]
        name = name_bytes.decode("utf-8")

        if name == member_name:
            return {
                "compression": compression,
                "crc32": crc32,
                "compressed_size": compressed_size,
                "uncompressed_size": uncompressed_size,
                "local_offset": local_offset,
            }
        offset += record_size

    raise FileNotFoundError(f"{member_name!r} is not present in the template archive.")


def extract_member(url: str, member_name: str, destination: Path) -> None:
    """Download, decompress, CRC-check, and atomically install one ZIP member."""

    info = find_member(url, member_name)
    local_offset = info["local_offset"]
    local_header = fetch_range(url, local_offset, local_offset + 29)
    if local_header[:4] != LOCAL_SIGNATURE:
        raise RuntimeError(f"{member_name!r} has an invalid ZIP local header.")

    local_name_length, local_extra_length = struct.unpack_from("<HH", local_header, 26)
    data_start = local_offset + 30 + local_name_length + local_extra_length
    data_end = data_start + info["compressed_size"] - 1
    destination.parent.mkdir(parents=True, exist_ok=True)
    partial = destination.with_suffix(destination.suffix + ".part")

    decompressor = None
    if info["compression"] == 8:
        decompressor = zlib.decompressobj(-zlib.MAX_WBITS)
    elif info["compression"] != 0:
        raise RuntimeError(
            f"Unsupported ZIP compression method {info['compression']} for {member_name}."
        )

    written = 0
    checksum = 0
    try:
        with request(url, byte_range=(data_start, data_end)) as response, partial.open(
            "wb"
        ) as output:
            while True:
                compressed = response.read(CHUNK_SIZE)
                if not compressed:
                    break
                data = decompressor.decompress(compressed) if decompressor else compressed
                output.write(data)
                written += len(data)
                checksum = binascii.crc32(data, checksum)

            if decompressor:
                data = decompressor.flush()
                output.write(data)
                written += len(data)
                checksum = binascii.crc32(data, checksum)

        checksum &= 0xFFFFFFFF
        if written != info["uncompressed_size"]:
            raise RuntimeError(
                f"Size mismatch for {member_name}: expected {info['uncompressed_size']}, "
                f"received {written}."
            )
        if checksum != info["crc32"]:
            raise RuntimeError(
                f"CRC mismatch for {member_name}: expected {info['crc32']:08x}, "
                f"received {checksum:08x}."
            )

        os.replace(partial, destination)
        print(
            f"Installed {member_name} -> {destination} "
            f"({written / (1024 * 1024):.1f} MiB, CRC32 {checksum:08x})"
        )
    except Exception:
        partial.unlink(missing_ok=True)
        raise


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", required=True, help="Official Godot TPZ URL")
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument(
        "files",
        nargs="+",
        help="Template filenames below the archive's templates/ directory",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    for filename in args.files:
        extract_member(
            args.url,
            f"templates/{filename}",
            args.output_dir / filename,
        )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Template download failed: {error}", file=sys.stderr)
        raise SystemExit(1)
