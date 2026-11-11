"""Fail early if native SQLite security floor or NuGet audit is removed."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]

class SQLiteNativeSecurityContract(unittest.TestCase):
    def test_desktop_explicit_bundle_overrides_affected_transitive_version(self):
        project=ET.parse(ROOT/"src/NajmCard.Desktop/NajmCard.Desktop.csproj").getroot()
        packages={node.attrib["Include"]:node.attrib["Version"]
                  for node in project.findall(".//PackageReference")}
        self.assertEqual(packages["Microsoft.Data.Sqlite"],"10.0.0")
        self.assertEqual(packages["SQLitePCLRaw.bundle_e_sqlite3"],"2.1.14")
        self.assertNotIn("SQLitePCLRaw.lib.e_sqlite3",packages)

    def test_high_and_critical_nuget_warnings_are_build_blocking(self):
        props=ET.parse(ROOT/"Directory.Build.props").getroot()
        found=props.find("./PropertyGroup/WarningsAsErrors")
        self.assertIsNotNone(found)
        self.assertIn("NU1903",found.text)
        self.assertIn("NU1904",found.text)

    def test_real_windows_test_checks_the_loaded_binary_not_package_name(self):
        path=ROOT/"tests/NajmCard.Desktop.Tests/NativeSqliteSafetyTests.cs"
        content=path.read_text(encoding="utf-8")
        self.assertIn('cmd.CommandText="SELECT sqlite_version()"',content)
        self.assertIn("new Version(3,50,2)",content)
        self.assertIn("loaded!.CompareTo(",content)
        self.assertIn('cmd.CommandText="PRAGMA integrity_check"',content)

if __name__=="__main__":unittest.main()
