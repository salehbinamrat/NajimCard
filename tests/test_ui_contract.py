"""Fast structural design regression checks. Real WPF compilation still runs on Windows CI."""
import pathlib
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
DESKTOP = ROOT / "src" / "NajmCard.Desktop"
XAML = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"

class UiDesignContract(unittest.TestCase):
    def test_xaml_files_well_formed(self):
        for path in DESKTOP.rglob("*.xaml"):
            with self.subTest(path=path):
                ET.parse(path)

    def test_every_section_has_a_distinct_nav_entry_and_backing_view(self):
        source = (DESKTOP / "MainWindow.xaml.cs").read_text(encoding="utf-8")
        key_view = {
            "dashboard":"DashboardView", "generator":"VoucherWorkspaceView",
            "cards":"CardsView", "profiles":"ProfilesView", "sessions":"SessionsView",
            "servers":"ServersView", "reports":"ReportsView",
            "activity":"ActivityView", "tools":"NetworkMaintenanceView", "settings":"SettingsView"
        }
        self.assertEqual(len(key_view), 10)
        for key, view in key_view.items():
            with self.subTest(key=key):
                self.assertEqual(len(re.findall(r'Entry\("' + key + r'",', source)), 1)
                if key=="dashboard":
                    self.assertIn('"dashboard" => _dashboardPage ??= new DashboardView(',source)
                else:
                    self.assertIn(f'"{key}" => new {view}(', source)
                source_files = list((DESKTOP / "Views").glob("*.cs"))
                self.assertTrue(any(re.search(rf'\bclass\s+{view}\b', p.read_text(encoding="utf-8")) for p in source_files))

    def test_operational_views_are_isolated_and_have_single_owners(self):
        # Architectural regression: a monolithic ~100kB view file made
        # UI modifications, reviews and feature isolation unnecessarily risky.
        view_names = (
            "GeneratorView", "CardsView", "ProfilesView", "SessionsView",
            "ServersView", "PrintingView", "ReportsView", "ToolsView",
            "SettingsView"
        )
        folder = DESKTOP / "Views"
        self.assertFalse((folder / "OperationsViews.cs").exists(),
                         "Remove the historical monolith after splitting classes")
        self.assertTrue((folder / "PageUi.cs").exists())
        sources = [(p, p.read_text(encoding="utf-8")) for p in folder.glob("*.cs")]
        for view in view_names:
            matches = [p.name for p, content in sources if
                       re.search(r"\bclass\s+" + view + r"\b", content)]
            expected={view + ".cs"}
            # GeneratorView's separate 37-line partial only carries package
            # navigation state; it is one WPF owner, not a duplicate view.
            if view=="GeneratorView":
                expected.add("GeneratorView.PackageNavigation.cs")
                expected.add("GeneratorView.LivePreview.cs")
                expected.update({"GeneratorView.CardDesign.cs",
                    "GeneratorView.ReferenceLayout.cs",
                    "GeneratorView.DesignerPanel.cs",
                    "GeneratorView.ProfileLoading.cs",
                    "GeneratorView.PageCount.cs",
                    "GeneratorView.SaveToast.cs"})
            if view=="PrintingView":
                expected.add("PrintingView.PrintSelectors.cs")
            self.assertEqual(set(matches), expected,
                             f"{view}: one main owner plus only explicitly reviewed partials")
        for path, content in sources:
            self.assertLessEqual(len(content.splitlines()),
                                     750 if path.name == "GeneratorView.cs" else 650,
                                 f"{path.name}: keep large view classes isolated and reviewable")

    def test_generation_and_printing_are_one_real_workflow(self):
        source=(DESKTOP / "MainWindow.xaml.cs").read_text(encoding="utf-8")
        self.assertEqual(len(re.findall(r'Entry\("generator"',source)),1)
        self.assertNotIn('Entry("printing"',source)
        self.assertIn('"generator" => new VoucherWorkspaceView(',source)
        combined=(DESKTOP / "Views" / "VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        creator=(DESKTOP / "Views" / "GeneratorView.cs").read_text(encoding="utf-8")
        printer=(DESKTOP / "Views" / "PrintingView.cs").read_text(encoding="utf-8")
        designer=(DESKTOP / "Views" / "GeneratorView.DesignerPanel.cs").read_text(encoding="utf-8")
        for option in ("باقة وسيرفر الإرسال","اسم المستخدم وكلمة المرور",
                       "معاينة وتصميم الكرت","عدد الكروت",
                       "إعداد الطباعة والدفعة",
                       "إنشاء وإرسال واحد",
                       "إنشاء وإرسال","معاينة قبل الإنشاء",
                       "ملف PDF","معاينة PDF","طباعة دفعة"):
            self.assertIn(option,combined+creator+printer+designer)
        self.assertIn("CreateAndSendAsync",combined)
        self.assertIn("Execute(PreviewDraftPdf)",combined)

    def test_colored_vector_icon_pack_is_complete(self):
        xaml = ET.parse(DESKTOP / "Themes" / "VectorIcons.xaml").getroot()
        icons = list(xaml.findall(f'{XAML}PathGeometry'))
        keys = [e.get('{http://schemas.microsoft.com/winfx/2006/xaml}Key') for e in icons]
        self.assertGreaterEqual(len(icons), 14)
        self.assertEqual(len(set(keys)), len(keys))
        view = (DESKTOP / "MainWindow.xaml.cs").read_text(encoding="utf-8")
        used = set(re.findall(r'Entry\("[a-z]+",\s*"(Icon\w+)"', view))
        self.assertTrue(used.issubset(set(keys)))
        self.assertEqual(len(used), 10)

    def test_dashboard_charts_derive_from_real_router_data(self):
        view=(DESKTOP / "Views" / "DashboardView.xaml.cs").read_text(encoding="utf-8")
        markup=(DESKTOP / "Views" / "DashboardView.xaml").read_text(encoding="utf-8")
        self.assertIn('ReadDashboardLiveCountsAsync',view)
        self.assertNotIn("RenderSegments",view)
        self.assertIn('ActivePercent.Text=Pct(',view)
        self.assertIn('RenderProfiles(profiles)',view)
        self.assertIn('"generator"',view)
        self.assertIn('"cards"',view)
        self.assertIn('"reports"',view)
        self.assertIn('ProfileBars',markup)
        self.assertNotIn('StatusRing',markup)
        self.assertIn('x:Name="ActiveLegend"',markup)
        self.assertIn('Transfer Limit',markup)

if __name__ == "__main__":
    unittest.main()
