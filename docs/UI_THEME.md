# OptiCopy Desktop UI Theme

The Windows client deliberately shares the visual language of the Android OptiCopy application.

Reference: `madmaxmehdi44/OptiCopy` theme files `Color.kt` and `Theme.kt`.

## Palette

Dark mode is based on the mobile Optical Cyber theme:

- Obsidian: `#0A0E17`
- Slate: `#0F172A`
- Surface: `#161E2E`
- Surface variant: `#1E293B`
- Elevated surface: `#243048`
- Neon cyan: `#00F0FF`
- Cyan glow: `#38BDF8`
- Neon emerald: `#10B981`
- Emerald glow: `#34D399`
- Neon amber: `#F59E0B`
- Electric blue: `#3B82F6`
- Neon purple: `#A855F7`
- Cyber red: `#EF4444`
- Primary text: `#F8FAFC`
- Secondary text: `#94A3B8`
- Muted text: `#64748B`
- Card border: `#334155`
- Highlight border: `#0284C7`

## Windows adaptation

The Windows client uses the same palette, but translates Material/Compose patterns into native WinUI 3 controls.

The application should feel like the same product family on Android and Windows while still behaving like a native Windows 11 application.

## Design rules

- Dark mode is the primary visual identity.
- Light mode remains supported and uses restrained, higher-contrast equivalents.
- Cyan represents the optical transfer / primary action.
- Emerald represents receiving / healthy / verified state.
- Amber represents active or attention-required state.
- Red represents errors.
- Purple is reserved for secondary technical tools and diagnostics.
- Avoid generic Material cards, gradients, and excessive decoration.
- Prefer restrained borders, compact typography, generous spacing, and high information density on transfer screens.
- QR presentation should remain the visual center of the sender experience.
- Camera/reticle UI should preserve the technical optical aesthetic of the mobile client.
