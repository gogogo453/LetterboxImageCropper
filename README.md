# Letterbox Image Cropper

Removes black bars (top/bottom, left/right, or both) from a folder of images. Originals are not changed; cropped copies are saved to a `_fixed` subfolder.

Supported files: `.jpg`, `.jpeg`, `.png`. Runs on 64-bit Windows; no .NET installation needed.

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

Single-file, self-contained exe (about 50 MB, written to `publish\`):

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

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
| `Codecs.cs` | Restricts decoding/encoding to JPEG and PNG |

### Versioning

The version is set in one place: `VersionPrefix` in `LetterboxImageCropper.csproj` (format `MAJOR.MINOR.PATCH`). It is shown in the window title and in the exe's file properties.

To release a new version:

1. Increase `VersionPrefix`: patch for fixes, minor for new features, major for breaking changes.
2. Add an entry to `CHANGELOG.md`.
3. Publish.

For a pre-release build, add a suffix when publishing, e.g. `-p:VersionSuffix=beta.1` (shown as `1.1.0-beta.1`).

### Third-party library

[SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) **3.1.12**, under the Six Labors Split License. It is deliberately pinned to 3.1.x: versions 3.2 and later require a Six Labors license key for Release builds. Only the JPEG and PNG codecs are enabled.
