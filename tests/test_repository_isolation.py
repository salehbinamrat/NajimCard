"""Regression: Hadramout must live in its own private repository.

NajmCard remains an independent .NET WPF project; no archived Hadramout
Python code, imagery or build workflows should be bundled into this repository.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]

class IndependentRepositoryTest(unittest.TestCase):
    def test_hadramout_is_not_nested_in_najmcard(self):
        self.assertFalse((ROOT / "Hadramout_Cards").exists(),
                         "Hadramout source belongs in salehbinamrat/Hadramout")

    def test_najmcard_remains_buildable_source_tree(self):
        self.assertTrue((ROOT / "NajmCard.slnx").is_file())
        self.assertTrue((ROOT / "src" / "NajmCard.Desktop").is_dir())
        self.assertTrue((ROOT / ".github" / "workflows" / "windows-ci.yml").is_file())

if __name__ == "__main__":
    unittest.main()
