# Changelog

All notable changes are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- Image processing now uses NetVips / libvips instead of SixLabors.ImageSharp. No license key is needed, and export is about 2.5× faster.
- Releases ship `libvips-42.dll` next to the exe, together with `THIRD-PARTY-NOTICES.md`.
- Indexed (palette) PNGs stay indexed when exported as source.
- JPEGs with 4:2:2 or other uncommon chroma subsampling are exported as 4:4:4 (no additional colour loss).

### Fixed
- Damaged or truncated images are now listed as unreadable instead of being processed with missing parts.

## [1.0.0] - 2026-11-18

First versioned release.

- Detects black bars automatically: top/bottom, left/right, or both.
- One crop per image size; preview with manual adjustment.
- Exports only images with something to crop; others are skipped with a reason.
- Output to a `_fixed` subfolder; same format and JPEG quality as the source, or JPEG/PNG.
- Supports JPG, JPEG and PNG.
