"""Protect functional WPF runtime while enforcing safe size reductions.

All package optimizations must be release-only. Core/desktop tests are still
run before creating four full self-contained packages in Windows CI.
"""
import pathlib
import unittest

ROOT=pathlib.Path(__file__).resolve().parents[1]
SCRIPT=(ROOT/"scripts/package.ps1").read_text(encoding="utf-8")
WORKFLOW=(ROOT/".github/workflows/windows-ci.yml").read_text(encoding="utf-8")
PROJECT=(ROOT/"src/NajmCard.Desktop/NajmCard.Desktop.csproj").read_text(encoding="utf-8")

class SafePackageCleanupTests(unittest.TestCase):
    def test_clean_only_arch_specific_publish_output(self):
        self.assertIn('Join-Path $outRoot "publish-$Arch"',SCRIPT)
        self.assertIn('Test-Path -LiteralPath $publishDir',SCRIPT)
        self.assertIn('Remove-Item -LiteralPath $publishDir -Recurse -Force',SCRIPT)
        self.assertNotIn('Remove-Item -Recurse -Force $outRoot',SCRIPT)
        self.assertIn('New-Item -ItemType Directory -Force -Path $publishDir',SCRIPT)

    def test_self_contained_reflection_and_localization_are_safe(self):
        self.assertIn('--self-contained true',SCRIPT)
        self.assertIn('-p:PublishTrimmed=false',SCRIPT)
        self.assertIn('-p:PublishReadyToRun=false',SCRIPT)
        self.assertIn('SatelliteResourceLanguages=ar%3Ben',SCRIPT)
        self.assertNotIn('-p:PublishTrimmed=true',SCRIPT)
        self.assertNotIn('PublishAot=true',SCRIPT)
        self.assertNotIn('PublishSingleFile=true',SCRIPT)
        self.assertIn('<UseWPF>true</UseWPF>',PROJECT)
        self.assertIn('QRCoder',PROJECT)
        self.assertIn('PDFsharp-WPF',PROJECT)
        self.assertIn('BouncyCastle.Cryptography',PROJECT)

    def test_symbols_only_removed_from_release_payload(self):
        self.assertIn('-p:DebugType=none',SCRIPT)
        self.assertIn('-p:DebugSymbols=false',SCRIPT)
        self.assertIn("Get-ChildItem -LiteralPath $publishDir -Filter '*.pdb' -File -Recurse",SCRIPT)
        self.assertIn('Remove-Item -Force',SCRIPT)

    def test_four_independently_verified_packages_after_tests(self):
        self.assertIn('needs: [windows-core, windows-release]',WORKFLOW)
        self.assertIn('matrix:',WORKFLOW)
        self.assertIn('arch: [x86, x64]',WORKFLOW)
        self.assertIn('Tests before packaging',WORKFLOW)
        self.assertIn('SHA256SUMS-',SCRIPT)
        self.assertIn('Portable.zip',SCRIPT)
        self.assertIn('Installer.exe',SCRIPT)

if __name__=="__main__":
    unittest.main()
