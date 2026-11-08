"""Windows portable branding must be an embedded executable icon, not just an installer shortcut.
Run in parallel Windows Core/Release tests before packaging. PNG/ICO dimensions verified by build script.
"""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
class PortableIconContract(unittest.TestCase):
    def test_application_has_compiled_win32_icon(self):
        cs=(ROOT/"src"/"NajmCard.Desktop"/"NajmCard.Desktop.csproj").read_text(encoding="utf8")
        self.assertIn("<ApplicationIcon>Assets\\NajmCard.ico</ApplicationIcon>",cs)
        self.assertIn('BeforeTargets="PrepareForBuild"',cs)
        self.assertIn("generate-icon.ps1",cs)
    def test_windows_installer_and_packages_share_icon(self):
        installer=(ROOT/"installer"/"NajmCard.iss").read_text(encoding="utf8")
        script=(ROOT/"scripts"/"package.ps1").read_text(encoding="utf8")
        self.assertIn("SetupIconFile=",installer)
        self.assertIn("UninstallDisplayIcon={app}\\NajmCard.exe",installer)
        self.assertIn("ExtractAssociatedIcon",script)
        self.assertIn("NajmCard.ico",script)
    def test_generator_is_vector_sized_and_separate_from_hadramout(self):
        script=(ROOT/"scripts"/"generate-icon.ps1").read_text(encoding="utf8")
        for tok in ("NajmCard.ico","System.Drawing","256","128","48","32","16","Icon", "FileMode"):
            self.assertIn(tok,script)
        self.assertNotIn("Hadramout_Cards",script)
if __name__=="__main__":unittest.main()
