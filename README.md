# Letterbox Image Cropper

Removes black bars (top/bottom, left/right, or both) from a folder of images. Originals are not changed; cropped copies are saved to a `_fixed` subfolder.

Supported files: `.jpg`, `.jpeg`, `.png`. Runs on 64-bit Windows; no .NET installation needed. Keep `libvips-42.dll` in the same folder as the exe.

## Usage

1. Start `LetterboxImageCropper.exe`.
2. Click **Browse…** and choose a folder (or drag the folder onto the window).
3. The app analyzes all images and shows the detected bars, the resulting frame size and a preview. The red area in the preview is what will be removed.
4. If needed, adjust the crop values or settings.
5. Click **Confirm & Export**. The cropped images are written to `<folder>\_fixed`.

If the folder contains images of different sizes, each size gets its own crop. Use the **Resolution** list to switch between them.

## Controls

- **Top / Bottom / Left / Right**: pixels to remove. Filled in automatically; you can change them.
- **Black threshold**: how dark a pixel must be to count as bar. Raise it if the bars are dark grey.
- **Noise allowance**: tolerance for stray bright pixels in a bar.
- **Extra trim**: removes a few more pixels from each bar, e.g. to get rid of a soft edge line.
- **Bars to detect**: automatic, top/bottom only, or left/right only.
- **Force symmetric bars**: use the same size for top and bottom (and for left and right).
- **Recalculate**: analyze the folder again. Needed after changing the threshold or noise allowance.
- **Format**: *Same as source* (recommended) keeps each file's format and JPEG quality. You can also export everything as JPEG with a chosen quality, or as PNG.

## Which images are exported

Only images marked **OK** in the list are exported. Images are skipped when:

- no black bars were found (nothing to crop),
- something like subtitles or a logo is inside the area that would be cut,
- the image is completely black,
- the file can't be read.

The export button shows how many images will be exported and is disabled if there are none. If `_fixed` already contains files with the same names, you can overwrite or skip them.

---

## Building from source

Requires the .NET 10 SDK (Windows).

```bash
dotnet build -c Release
```

Self-contained release build, written to `publish\`:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```

The output is `LetterboxImageCropper.exe` (about 50 MB), `libvips-42.dll` (about 18 MB), `LICENSE`, `THIRD-PARTY-NOTICES.md` and the `licenses` folder. Distribute them together. The DLL is intentionally kept as a separate file (see below).

### Project structure

| File | Purpose |
|---|---|
| `Program.cs` | Entry point (optional folder argument) |
| `AppInfo.cs` | Application name and version (read from the project file) |
| `MainForm.cs` | User interface |
| `PreviewPanel.cs`, `PreviewImage.cs` | Preview rendering |
| `BarDetector.cs` | Per-image bar detection |
| `CropAnalyzer.cs` | Grouping by resolution, voting, image statuses |
| `ImageExporter.cs` | Cropping, encoding, metadata |
| `ImageFiles.cs` | Folder scan (natural sort order) |
| `Codecs.cs` | Image I/O through libvips; only the JPEG and PNG loaders are enabled |
| `JpegHeader.cs` | Reads a JPEG's quality and chroma subsampling from its header |

### Third-party libraries

- [NetVips](https://github.com/kleisauke/net-vips) **3.2.0** (MIT): .NET binding for libvips.
- [libvips](https://www.libvips.org/) **8.18.7**, from the `NetVips.Native.win-x64` package (LGPL-3.0-or-later, with bundled libraries under their own licenses). It is loaded from the separate `libvips-42.dll`, which users may replace, as the LGPL requires.

Details, licenses and source locations are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Keep `NetVips` and `NetVips.Native.win-x64` on matching versions when updating.
