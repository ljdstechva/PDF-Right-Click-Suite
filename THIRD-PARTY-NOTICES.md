# Third-Party Notices

PDF Right Click Suite includes third-party software. These notices apply only to those third-party components and do not grant a license to this repository's original source code.

## Runtime components

| Component | Version | Declared license | Upstream |
| --- | --- | --- | --- |
| PDFsharp | 6.2.4 | MIT | https://github.com/empira/PDFsharp |
| PDFtoImage | 5.2.1 | MIT | https://github.com/sungaila/PDFtoImage |
| bblanchon.PDFium.Win32 | 147.0.7690 | Apache-2.0 in NuGet package metadata | https://github.com/bblanchon/pdfium-binaries |
| SkiaSharp and SkiaSharp.NativeAssets.Win32 | 4.148.0 | MIT, plus incorporated third-party licenses | https://github.com/mono/SkiaSharp |
| Spectre.Console and Spectre.Console.Ansi | 0.57.1 | MIT | https://github.com/spectreconsole/spectre.console |
| .NET runtime and Microsoft.Extensions/System assemblies | 8.0.x | MIT, plus incorporated third-party licenses | https://github.com/dotnet/runtime |

The release package also includes these unmodified upstream files:

- `third-party/DOTNET-RUNTIME-LICENSE.txt`
- `third-party/DOTNET-RUNTIME-THIRD-PARTY-NOTICES.txt`
- `third-party/SKIASHARP-LICENSE.txt`
- `third-party/SKIASHARP-THIRD-PARTY-NOTICES.txt`

The SkiaSharp third-party notice file reproduces the complete Apache License 2.0 text in addition to the licenses and copyright notices for material incorporated into SkiaSharp. The PDFium binary package's NuGet metadata identifies its license as Apache-2.0 and identifies Benoît Blanchon as copyright holder for the package.

## MIT notices

### PDFsharp 6.2.4

Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany

### PDFtoImage 5.2.1

Copyright (c) 2021-2025 David Sungaila

### Spectre.Console and Spectre.Console.Ansi 0.57.1

Copyright (c) 2020 Patrik Svensson, Phil Scott, Nils Andresen

### MIT license text for the components above

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

## PDFium package attribution

`bblanchon.PDFium.Win32` 147.0.7690 package metadata:

- Copyright © Benoît Blanchon 2017-2025
- Declared license: Apache-2.0
- License text: https://www.apache.org/licenses/LICENSE-2.0
- Package source commit: https://github.com/bblanchon/pdfium-binaries/tree/9d8f70d3f4c0d37c0a479407805ceab4dd68d516
