"""Safely restore the independent NajmCard C# source snapshot into a PR branch."""
import hashlib
import pathlib
import shutil
import sys
import zipfile

EXPECTED = "9b235f87f629c8116fe4c04ec4298aca50e01bb6ea2a270155414b158b0857e3"
EXPECTED_FILES = 35
ALLOWED_SUFFIXES = {
    ".slnx",".csproj",".cs",".xaml",".props",".json",".md",".yml",".yaml",
    ".iss",".ps1",".py",
}
ALLOWED_EXACT = {".gitignore", "README.md"}
ROOT = pathlib.Path(__file__).resolve().parents[1]

def main(archive: pathlib.Path) -> None:
    if not archive.is_file():
        raise SystemExit("Source archive missing")
    actual = hashlib.sha256(archive.read_bytes()).hexdigest()
    if actual != EXPECTED:
        raise SystemExit("Source archive SHA-256 differs from the reviewed snapshot")
    with zipfile.ZipFile(archive) as source:
        if source.testzip():
            raise SystemExit("Damaged source archive")
        entries = [item for item in source.infolist() if not item.is_dir()]
        if len(entries) != EXPECTED_FILES:
            raise SystemExit(f"Expected {EXPECTED_FILES} files, found {len(entries)}")
        for item in entries:
            posix = pathlib.PurePosixPath(item.filename)
            if len(posix.parts) < 2 or posix.parts[0] != "NajmCard":
                raise SystemExit(f"Unexpected root: {posix}")
            rel = pathlib.PurePosixPath(*posix.parts[1:])
            if (posix.is_absolute() or '..' in posix.parts or '\\' in item.filename
                    or rel.suffix not in ALLOWED_SUFFIXES and rel.name not in ALLOWED_EXACT):
                raise SystemExit(f"Unsafe source archive entry: {posix}")
            if (item.external_attr >> 16 & 0o170000) == 0o120000:
                raise SystemExit("Symlink entries are not permitted")
            if item.file_size > 1_000_000:
                raise SystemExit("Unexpected oversized source entry")
            destination = ROOT.joinpath(*rel.parts)
            if destination.exists() and destination.read_bytes() != source.read(item):
                if str(rel) == "README.md":
                    # Keep repo-wide source status and do not overwrite main README.
                    continue
                raise SystemExit(f"Conflict with existing branch file: {rel}")
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(source.read(item))
    print(f"Imported {len(entries)} approved C# project files")

if __name__ == "__main__":
    if len(sys.argv)!=2:
        raise SystemExit("Usage: python scripts/import_initial_source.py <zip>")
    main(pathlib.Path(sys.argv[1]))
