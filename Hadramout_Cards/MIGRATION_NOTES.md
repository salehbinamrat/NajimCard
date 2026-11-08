# Hadramout Cards source migration — verified complete
Source repository: `salehbinzain83-star/Hadramout-cards`
Source commit: `f5973dea4d3bb35ba18e67852c824ccf7634bd5f`

**Complete: 63/63 original files migrated with byte-for-byte matching Git blob SHA-1 identities.**

The two large artwork assets were recovered unchanged from the successful Windows x64 Portable artifact, checked against the exact source Git blob SHAs, and imported via a temporary GitHub Actions job that returned success. The job removed its own temporary workflow file afterward.

- `assets/default_card.png` (1,262,691 bytes, `c1bcb76cf37d626e39bd65e72e45ddd2286f39bf`)
- `assets/login_hero.png` (1,153,878 bytes, `9c5fbc6117eeef514f78f4ac9a87dcb417e915f0`)

Hadramout's code, tests, artwork and packaging scripts are intentionally isolated in `Hadramout_Cards/` and do not replace or mix with NajmCard's WPF code. The original nested workflow is a historical source file and is not activated as a top-level GitHub Actions workflow.

**Important:** The source repository describes Hadramout v1.1.3 as a partially recovered Python implementation. Exact source transfer does not imply feature parity or a tested release. Verify Hadramout's own source completeness and run its native Windows tests before using or publishing it.

The import branch stays separate from `main` until the NajmCard Windows Core and Windows Release jobs succeed on the same SHA, package x86/x64 Portable and Installer, and SHA-256 artifacts are validated. No original source repository was modified.
