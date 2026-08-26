# OptiCopy Desktop UI Theme

The Windows client shares the visual identity of the Android OptiCopy application and translates it into native WinUI 3.

The Android source of truth is `app/src/main/java/com/example/ui/theme/Color.kt` and `Theme.kt` in `madmaxmehdi44/OptiCopy`. The mobile palette uses Obsidian/Slate surfaces, Neon Cyan as primary, Neon Emerald as secondary, Neon Amber as tertiary, and high-contrast slate typography. 

## Palette

Dark mode:
- Obsidian `#0A0E17`
- Slate `#0F172A`
- Surface `#161E2E`
- Surface Variant `#1E293B`
- Elevated Surface `#243048`
- Neon Cyan `#00F0FF`
- Cyan Glow `#38BDF8`
- Neon Emerald `#10B981`
- Emerald Glow `#34D399`
- Neon Amber `#F59E0B`
- Electric Blue `#3B82F6`
- Neon Purple `#A855F7`
- Cyber Red `#EF4444`
- Primary Text `#F8FAFC`
- Secondary Text `#94A3B8`
- Muted Text `#64748B`
- Card Border `#334155`
- Highlight Border `#0284C7`

Light mode uses restrained, higher-contrast equivalents while preserving the same semantic colors.

## Windows adaptation

The Windows application should feel like the same OptiCopy product family on Android, while retaining native Windows behavior:

- WinUI 3 controls and navigation
- Windows 11 title bar and windowing
- Mica/Acrylic only when useful
- Native keyboard and focus behavior
- Dark mode as the primary visual identity
- Light mode supported
- Cyan = optical transfer / primary action
- Emerald = receiving / healthy / verified
- Amber = active / attention
- Red = error
- Purple = diagnostics / technical tools

The main transfer UI should remain information-dense but calm. QR display and camera reticle experiences should visually connect to the existing mobile `QrDisplayCard`, `OpticalReticleOverlay`, `SenderScreen`, and `ReceiverScreen` concepts while being redesigned for desktop dimensions.