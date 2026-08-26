# OptiCopy Desktop

Native Windows desktop implementation of OptiCopy using C#/.NET and WinUI 3.

## Architecture

- `src/OptiCopy.Core` — platform-independent optical transfer protocol and fountain codec
- `src/OptiCopy.Imaging` — QR encoding/decoding and camera integration
- `src/OptiCopy.Data` — local persistence
- `src/OptiCopy.Windows` — WinUI 3 desktop application
- `tests/OptiCopy.Tests` — protocol and interoperability tests

## Reference implementation

The optical transfer codec is being ported from `bashalarmistalt/decimen-optical-transfer` with the goal of preserving wire compatibility while keeping the Windows implementation native and maintainable.

## Status

Initial architecture and C# core port in progress.
