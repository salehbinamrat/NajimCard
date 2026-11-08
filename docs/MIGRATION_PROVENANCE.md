# Independent repositories — source provenance

## NajmCard / NajimCard
- Repository: `salehbinamrat/NajimCard`
- Original source: `salehbinzain83-star/NajmCard` at `ffefe0ab8aa5ede7e9c4f589cb54f83887137a30`.
- Root-level NajmCard .NET 10/WPF implementation preserved.
- Windows Core and Release x86/x64 and 4 packages succeeded at migration SHA `74cc9e84eea7b842948266df4eff692f2d5d5c53` and `main` merge SHA `cdd5bf0a577648e9d4b7fa07def69cbbfe195084`.

## Hadramout Cards (separate repository)
- Independent PRIVATE repository: https://github.com/salehbinamrat/Hadramout
- Original source: `salehbinzain83-star/Hadramout-cards` at `f5973dea4d3bb35ba18e67852c824ccf7634bd5f`
- Exactly 63 of 63 original source file Git blob SHAs matched in the new Hadramout import branch, including 17 binary assets.
- The nested `Hadramout_Cards/` copy previously present in NajimCard has been removed on this branch to make the two applications independently versioned.
- Hadramout is identified by its original README as partially recovered Python 1.1.3 source: passing automated tests does not prove restored feature parity.

**Changes do not alter NajmCard runtime files or the existing paid-license interoperability logic.** Each repository must run its own CI before release, with tests first, then x86/x64 Portable and Installer. No merge on any failure.
