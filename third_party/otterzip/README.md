# Vendored OtterZip native archive engine

`x64/otterzip_ffi.dll` is a prebuilt binary of the OtterZip Rust core
(<https://github.com/LumiBearStudio/OtterZip>), vendored so SPAN can open the
archive formats `System.IO.Compression` cannot: `.7z`, `.tar.*`, `.xz`, `.bz2`,
`.gz`, `.cab`, `.rar`, and others.

Exact version, commit and checksum: [`VERSION.txt`](VERSION.txt).

## Why the DLL is vendored instead of built from source

`libz-ng-sys` (pulled in by `flate2` and the `zip` crate for deflate speed)
requires CMake, which is not on `PATH` on the release machine — it only exists
inside the Visual Studio install. Making the SPAN release depend on a working
Rust + CMake toolchain would put an avoidable failure mode in the release path.
A pinned binary with a recorded commit SHA is reproducible enough and keeps the
release simple.

## Scope: x64 only

The Rust core does not support Windows ARM64 or 32-bit targets
(OtterZip `docs/01-plan/performance.md` §7.2), so `Span.csproj` includes this
DLL only when `Platform == x64`. Non-x64 builds must fall back to the
`System.IO.Compression` path, and a failed load must be handled as a normal
code path — not an exception the user ever sees.

## Updating the DLL

1. In the OtterZip working tree, check out the tag you intend to ship and make
   sure `git status` is clean, so the commit SHA actually describes the binary.

2. Build with the static CRT. CMake must be on `PATH`:

   ```bash
   export PATH="/c/Program Files/Microsoft Visual Studio/2022/Professional/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin:$PATH"
   RUSTFLAGS="-C target-feature=+crt-static" cargo build -p otterzip-ffi --release
   ```

   **The `RUSTFLAGS` are not optional.** Without them the DLL imports
   `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll` and `MSVCP140.dll`. Those are present
   on any machine with Visual Studio installed, so the build looks fine here and
   fails on a clean Windows install — the app cannot load the DLL and dies at
   launch. OtterZip failed Microsoft Store certification this way on 2026-07-21
   under policy 10.1.2.10 ("product crashes at launch").

   `.cargo/config.toml` in OtterZip deliberately leaves `crt-static` out of the
   global config, because the C `zlib-ng` dependency's debug asserts break a
   static link of the test executables. It has to come from `RUSTFLAGS` on the
   release build.

3. Copy `target/release/otterzip_ffi.dll` into `x64/`.

4. Update [`VERSION.txt`](VERSION.txt) — tag, commit SHA, size, sha256, build
   date, and the ABI version reported by `otterzip_abi_version()`.

5. Run the gate. It must print `PASSED`:

   ```bash
   powershell -NoProfile -ExecutionPolicy Bypass -File third_party/otterzip/verify-otterzip-dll.ps1
   ```

   `build-msix.bat` runs this automatically and aborts the release if it fails.

## Before shipping RAR support

The binary statically links the `unrar` / `unrar_sys` crates, which embed UnRAR
sources © Alexander Roshal. SPAN uses UnRAR to **extract** RAR archives only and
must never create them, in any release, listing, or documentation.

Shipping this DLL therefore requires, in SPAN's own files:

- an **UnRAR Exception** in `LICENSE.md` — SPAN is GPL-3.0-or-later, and the
  UnRAR licence adds a restriction that GPL §7 does not permit to be imposed on
  recipients. OtterZip's `app/LICENSE` limits its own exception to `app/**`, so
  it does not cover SPAN; SPAN needs its own.
- the UnRAR licence §2 paragraph reproduced verbatim in the notices,
- OtterZip's `THIRD-PARTY-NOTICES.md` merged into or linked from
  `OpenSourceLicenses.md`,
- the OtterZip commit SHA recorded in the GitHub Release notes, which is what
  GPL §6(d) means by clear directions to the corresponding source.
