"""Photographed Hadramout responsive dashboard and voucher-editor layout contract.
These checks are structural; the Windows WPF build and real GUI tests are separate.
"""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
UI=ROOT/"src"/"NajmCard.Desktop"
NS={"w":"http://schemas.microsoft.com/winfx/2006/xaml/presentation"}
XNS="{http://schemas.microsoft.com/winfx/2006/xaml}"

class CompactScreenLayoutContract(unittest.TestCase):
    def test_main_window_defaults_to_requested_dimensions(self):
        x=ET.parse(UI/"MainWindow.xaml").getroot()
        self.assertEqual(float(x.attrib["Width"]),1200)
        self.assertEqual(float(x.attrib["Height"]),780)
        self.assertIsNotNone(x.find(".//w:RowDefinition[@"+XNS+"Name='WorkspaceHeaderRow']",NS))
        code=(UI/"MainWindow.xaml.cs").read_text(encoding="utf-8")
        self.assertIn("WorkspaceHeaderRow.Height",code)
        self.assertIn("isDashboard",code)

    def test_dashboard_has_compact_non_scrolling_grid_and_percentages(self):
        x=ET.parse(UI/"Views"/"DashboardView.xaml").getroot()
        self.assertFalse(x.findall(".//w:ScrollViewer",NS),
            "Homepage must not display a horizontal or vertical scroll indicator")
        self.assertIsNotNone(x.find(".//w:Grid",NS))
        body=(UI/"Views"/"DashboardView.xaml").read_text(encoding="utf-8")
        for name in ("ActivePercent","ExpiredPercent","ValidPercent",
                     "ProfileBars","ActivityRows","RefreshHistory",
                     "LifecycleButton","OnlineBreakdown","CpuProgress"):
            self.assertIn('x:Name="'+name+'"',body)
        self.assertEqual(body.count('Text="الكروت النشطة"'),0)
        self.assertEqual(body.count('Text="الكروت المنتهية"'),0)
        self.assertIn('Text="الرئيسية"',body)
        self.assertIn('Text="آخر النشاط',body)
        code=(UI/"Views"/"DashboardView.xaml.cs").read_text(encoding="utf-8")
        for label in ("ActivePercent.Text","ExpiredPercent.Text","ValidPercent.Text"):
            self.assertIn(label,code)

    def test_creator_has_fixed_footer_and_zero_page_scrolls(self):
        view=(UI/"Views"/"VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        self.assertIn("new ScrollViewer",view)
        self.assertIn("HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled",view)
        self.assertNotIn("StretchDirection.DownOnly",view)
        self.assertIn("GridLength(1,GridUnitType.Star)",view)
        self.assertIn('MakeButton("إنشاء وإرسال وحفظ"',view)
        self.assertIn('MakeButton("طباعة الدفعة"',view)
        generator=(UI/"Views"/"GeneratorView.cs").read_text(encoding="utf-8")
        self.assertIn("BuildSampleVoucherVisual",generator)
        self.assertIn("CompactSurface(",generator)
        self.assertIn("DefaultTemplate",generator)
        printer=(UI/"Views"/"PrintingView.cs").read_text(encoding="utf-8")
        self.assertIn("embedded",printer)
        self.assertIn("Visibility.Collapsed",printer)

    def test_dropdowns_use_burgundy_background_and_cream_foreground(self):
        page=(UI/"Views"/"VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        theme=(UI/"Themes"/"BurgundyGold.xaml").read_text(encoding="utf-8")
        self.assertIn('FindResource("DarkComboBox")',page)
        self.assertIn('TargetType="ComboBoxItem"',theme)
        self.assertIn('x:Name="PART_Popup"',theme)
        self.assertNotIn("#FFF5E7",page)

if __name__=="__main__":unittest.main()
