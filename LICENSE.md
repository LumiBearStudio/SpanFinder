# License

## Source Code — GNU General Public License v3.0

Copyright (C) 2026 LumiBear Studio

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program. If not, see <https://www.gnu.org/licenses/>.

### Microsoft Store Distribution Exception

As a special exception, the copyright holder grants permission to distribute the official binary of this program through the Microsoft Store, subject to the Microsoft Store's terms and conditions, without those terms being considered "additional restrictions" under Section 7 of the GPL v3. This exception applies **only** to the official distribution by the copyright holder (LumiBear Studio) and does not extend to third-party forks or redistributions.

For the avoidance of doubt, the scope limitation in the preceding paragraph applies to this Microsoft Store Distribution Exception only. It does not narrow the UnRAR Exception stated below.

---

### UnRAR Exception

As a special exception, LumiBear Studio, the copyright holder of SPAN Finder, gives you permission to combine, link with, dynamically load, bundle, and convey this program together with the UnRAR code — including UnRAR code in already compiled (binary) form, such as the prebuilt `otterzip_ffi.dll` archive engine built from the OtterZip project (<https://github.com/LumiBearStudio/OtterZip>), which statically links the `unrar` and `unrar_sys` crates and thereby embeds UnRAR sources © Alexander Roshal — and to convey the resulting combined work under the GNU General Public License, version 3 or (at your option) any later version, provided that you also comply with the UnRAR license terms as to the UnRAR portions.

This permission applies regardless of whether the UnRAR code was compiled by LumiBear Studio or obtained in prebuilt form from any source.

**This permission is granted to every recipient of this program, on equal terms.** It is **not** limited to official builds or official distributions by LumiBear Studio: it applies to forks, modified versions, and third-party redistributions alike.

This exception is necessary because the UnRAR license is not compatible with the GNU GPL: it does not allow the UnRAR sources to be used to develop a RAR (WinRAR) compatible archiver or to re-create the RAR compression algorithm, which GPL v3 sections 7 and 10 treat as a "further restriction" that may not be imposed on recipients. The exception exists solely so that the combined work may be conveyed, and it follows the approach 7-Zip uses ("GNU LGPL with unRAR restriction"). It does not relicense UnRAR, and it does not enlarge the rights that Alexander Roshal grants you in the UnRAR portions.

SPAN Finder uses UnRAR to **extract** RAR archives only; SPAN Finder never creates RAR archives. Note that the UnRAR license itself does not permit the UnRAR code to be used to develop a RAR (WinRAR) compatible archiver or to re-create the RAR compression algorithm.

If you modify this program, you may extend this exception to your version of the program, but you are not obliged to do so. If you do not wish to do so, delete this exception statement from your version.

#### UnRAR license — required paragraph, reproduced verbatim

> UnRAR source code may be used in any software to handle
> RAR archives without limitations free of charge, but cannot be
> used to develop RAR (WinRAR) compatible archiver and to
> re-create RAR compression algorithm, which is proprietary.
> Distribution of modified UnRAR source code in separate form
> or as a part of other software is permitted, provided that
> full text of this paragraph, starting from "UnRAR source code"
> words, is included in license, or in documentation if license
> is not available, and in source code comments of resulting package.

The complete UnRAR license, along with the other notices for components bundled
in the native archive engine, is in [OpenSourceLicenses.md](OpenSourceLicenses.md).

#### No warranty for the UnRAR portions

> THE RAR ARCHIVER AND THE UnRAR UTILITY ARE DISTRIBUTED "AS IS".
> NO WARRANTY OF ANY KIND IS EXPRESSED OR IMPLIED. YOU USE AT
> YOUR OWN RISK. THE AUTHOR WILL NOT BE LIABLE FOR DATA LOSS,
> DAMAGES, LOSS OF PROFITS OR ANY OTHER KIND OF LOSS WHILE USING
> OR MISUSING THIS SOFTWARE.

---

## Trademark & Brand Assets — All Rights Reserved

The following are **NOT** covered by the GPL v3 license above and remain the exclusive property of the copyright holder:

- **"SPAN Finder"** name and all related branding (including "SPAN", "Span Finder")
- **Official application icon and logo** (`Assets/app.ico` and any derived artwork)

These trademarks and brand assets may **not** be used without prior written permission from the copyright holder, including but not limited to:

- Publishing or distributing an application under the "SPAN Finder" name (or confusingly similar names) on the Microsoft Store, GitHub Releases, or any other distribution platform
- Using the official logo/icon in forks, derivative works, or third-party distributions
- Any use that implies official endorsement or affiliation

**If you fork or redistribute this software**, you **must**:

1. Choose a different application name
2. Replace all logo/icon assets with your own
3. Clearly indicate that your version is a modified fork and is not affiliated with the original SPAN Finder project

This restriction exists solely to prevent user confusion and protect the identity of the original project. It does not limit any rights granted by the GPL v3 for the source code itself.

---

## Third-Party Licenses

This project uses third-party libraries, each governed by their own licenses.
See [OpenSourceLicenses.md](OpenSourceLicenses.md) for the full list, including the
native archive engine (`otterzip_ffi.dll`) and the components bundled inside it.

### Corresponding source for the bundled native engine

`otterzip_ffi.dll` ships as a prebuilt binary. Its complete corresponding source is the
OtterZip repository at <https://github.com/LumiBearStudio/OtterZip>, at the commit
recorded in [`third_party/otterzip/VERSION.txt`](third_party/otterzip/VERSION.txt) and
restated in the release notes of each SPAN Finder release that bundles it.
