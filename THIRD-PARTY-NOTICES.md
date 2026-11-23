# Third-party notices

Letterbox Image Cropper is licensed under the MIT License (see `LICENSE`). It uses the following third-party software.

## NetVips 3.2.0

.NET binding for libvips — https://github.com/kleisauke/net-vips

```
MIT License

Copyright (c) 2018-present Kleis Auke Wolthuizen

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## libvips 8.18.7 and bundled libraries (`libvips-42.dll`)

The file `libvips-42.dll` next to the program is the prebuilt libvips binary from the NuGet package
`NetVips.Native.win-x64` 8.18.7, built by https://github.com/kleisauke/libvips-packaging.
It is used as a separate, dynamically loaded library. The package as a whole is licensed under the
**GNU Lesser General Public License v3.0 or later** (LGPL-3.0-or-later).

The full license texts are included in the `licenses` folder:
[`licenses/LGPL-3.0.txt`](licenses/LGPL-3.0.txt) (GNU LGPL v3) and
[`licenses/GPL-3.0.txt`](licenses/GPL-3.0.txt) (GNU GPL v3, which the LGPL v3 supplements).

| Library | Version | Used under the terms of |
|---|---|---|
| libvips | 8.18.7 | LGPLv3 (via the "any later version" clause of LGPLv2.1) |
| aom | 3.15.1 | BSD 2-Clause + Alliance for Open Media Patent License 1.0 |
| cairo | 1.18.6 | Mozilla Public License 1.1 |
| cgif | 0.5.4 | MIT License |
| expat | 2.8.5 | MIT License |
| fontconfig | 2.18.3 | fontconfig License (BSD-like) |
| freetype | 2.14.3 | FreeType License (BSD-like) |
| fribidi | 1.0.17 | LGPLv3 |
| glib | 2.90.0 | LGPLv3 |
| harfbuzz | 14.5.0 | MIT License |
| highway | 1.4.0 | BSD 3-Clause |
| lcms2 | 2.19.1 | MIT License |
| libarchive | 3.8.9 | BSD 2-Clause |
| libexif | 0.6.26 | LGPLv3 |
| libffi | 3.8.0 | MIT License |
| libheif | 1.23.5 | LGPLv3 |
| libimagequant | 2.4.1 | BSD 2-Clause |
| libnsgif | — | MIT License |
| libpng | 1.6.58 | libpng License |
| librsvg | 2.63.2 | LGPLv3 |
| libtiff | 4.7.2 | libtiff License (BSD-like) |
| libultrahdr | 2.0.2 | MIT License |
| libwebp | 1.6.0 | New BSD License |
| libxml2 | 2.15.4 | MIT License |
| mozjpeg | 0826579 | zlib License, IJG License, BSD 3-Clause |
| pango | 1.58.2 | LGPLv3 |
| pixman | 0.46.4 | MIT License |
| proxy-libintl | 0.5 | LGPLv3 |
| zlib-ng | 2.3.3 | zlib License |

The table follows the `THIRD-PARTY-NOTICES.md` and `versions.json` files shipped inside the
`NetVips.Native.win-x64` package.

### Source code

- libvips: https://github.com/libvips/libvips (release v8.18.7)
- Build scripts used to produce `libvips-42.dll`, including the exact versions and download locations of all
  bundled libraries: https://github.com/kleisauke/libvips-packaging (release matching 8.18.7)

### Replacing the library

`libvips-42.dll` is not compiled into the program. You may replace it with your own build of a compatible
libvips 8.18.x for 64-bit Windows (same file name) and the program will use it.
