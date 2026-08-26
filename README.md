# OptiCopy Desktop

Native Windows desktop client for OptiCopy.

## Stack

- C# / .NET 10
- WinUI 3 / Windows App SDK
- Native Windows UX with MVVM
- Platform-independent optical transfer core
- Decimen v3 systematic-carousel fountain coding

## Visual identity

The Windows client intentionally shares the Android OptiCopy application's **Optical Cyber** visual system: obsidian/slate surfaces, neon cyan primary actions, emerald receiver/verified states, amber attention states, restrained borders, and technical high-contrast transfer screens.

See [`docs/UI_THEME.md`](docs/UI_THEME.md).

## Repository structure

```text
src/
  OptiCopy.Core/       Protocol and fountain codec
  OptiCopy.Data/       Local persistence abstractions
  OptiCopy.Imaging/    QR and camera abstractions
  OptiCopy.Windows/    WinUI 3 application

tests/
  OptiCopy.Tests/      Core and interoperability tests
```

## Current status

The repository contains the native Windows foundation and the first C# port of the Decimen optical-transfer core. The next implementation stages are QR rendering/decoding, Windows camera capture, sender/receiver workflows, transfer history, settings, interoperability vectors, and MSIX packaging.

The Decimen reference project is available at:
https://github.com/bashalarmistalt/decimen-optical-transfer

Its current release is licensed AGPL-3.0-or-later; review licensing before distributing derived code.
