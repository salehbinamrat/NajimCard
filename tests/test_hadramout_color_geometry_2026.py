"""Acceptance regressions for Hadramout-layout NajmCard designer and dashboard.
Visual geometry asserts structure; WPF compilation remains the real executable gate.
"""
from pathlib import Path
import re
import unittest
BASE=Path(__file__).resolve().parents[1]
V=BASE/"src"/"NajmCard.Desktop"/"Views"
T=BASE/"src"/"NajmCard.Desktop"/"Themes"
M=BASE/"src"/"NajmCard.Core"/"Models"
class PrintPageGeometry(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.g=(V/"GeneratorView.cs").read_text(encoding="utf-8")
        cls.w=(V/"VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        cls.p=(V/"PrintingView.cs").read_text(encoding="utf-8")
        cls.d=(V/"DashboardView.xaml").read_text(encoding="utf-8")
        cls.style=(T/"BurgundyGold.xaml").read_text(encoding="utf-8")
        cls.ap=(M/"VoucherCardAppearance.cs").read_text(encoding="utf-8")
    def test_reference_geometry_two_rows_two_columns(self):
        self.assertIn("CreateReferenceLayout(",self.g)
        layout=(V/"GeneratorView.ReferenceLayout.cs").read_text(encoding="utf-8")
        self.assertIn("Grid.SetColumnSpan(routeSurface, 2)",layout)
        self.assertIn("Grid.SetColumn(designSurface, 1)",layout)
        self.assertIn("Grid.SetRow(formatSurface, 1)",layout)
        self.assertIn("Grid.SetColumn(printColumn, 0)",layout)
        self.assertIn("Grid.SetRow(designSurface, 2)",layout)
        self.assertIn("layout.Children.Add(designSurface)",layout)
        self.assertIn("Grid.SetRow(printColumn, 2)",layout)
        self.assertIn("layout.Children.Add(printColumn)",layout)
        self.assertNotIn('fields.Width=1140;',self.w)
        self.assertNotIn('StretchDirection=StretchDirection.DownOnly',self.w)
    def test_reference_colors_and_no_overflow_areas(self):
        sections=self.g+(V/"GeneratorView.DesignerPanel.cs").read_text(encoding="utf-8")+(V/"GeneratorView.ReferenceLayout.cs").read_text(encoding="utf-8")
        self.assertNotIn("#F8EFD9",sections)
        self.assertIn("Color.FromRgb(132, 15, 55)",sections)
        self.assertIn("#48192A",sections)
        self.assertIn("Color.FromRgb(69, 9, 27)",sections)
        self.assertIn('FindResource("DarkComboBox")',self.w)
        self.assertNotIn("#FFF5E7",self.w)
        self.assertIn("Arial",self.style)
    def test_template_image_is_persistent_and_printed_not_fake_preview(self):
        self.assertIn("TemplateImagePath",self.ap)
        self.assertIn("ChooseTemplateImage",(V/"GeneratorView.ReferenceLayout.cs").read_text(encoding="utf-8"))
        self.assertIn("TemplateImagePath",self.p)
        self.assertIn("OpenFileDialog",(V/"GeneratorView.ReferenceLayout.cs").read_text(encoding="utf-8"))
        self.assertIn("SaveCurrentPackage()",self.g)
    def test_credentials_options_and_server_selection(self):
        self.assertIn("اسم مستخدم فقط",self.g)
        self.assertIn("اسم مستخدم وكلمة مرور",self.g)
        self.assertIn("متطابقتين",self.g)
        self.assertIn("مختلفتين",self.g)
        discovery=(V/"GeneratorView.ProfileLoading.cs").read_text(encoding="utf-8")
        self.assertIn("ReadServersAsync(",discovery)
        self.assertIn("حساب مدير User Manager v6",self.g)
    def test_dashboard_no_donut_and_lifecycle_grid(self):
        self.assertNotIn("<Ellipse ",self.d)
        self.assertNotIn('x:Name="StatusRing"',self.d)
        for name in ("ActiveLegend","ExpiredLegend","DisabledLegend",
                     "ActivePercent","ExpiredPercent","ValidPercent",
                     "عرض النشطة","عرض المنتهية"):
            self.assertIn(name,self.d)
        self.assertIn('x:Name="RouterConnectionStatus"',self.d)
        self.assertIn('Text="الرئيسية"',self.d)
        self.assertIn('x:Name="ProfileBars"',self.d)
        self.assertIn('x:Name="ActivityRows"',self.d)
        self.assertIn('تعطيل الهوتسبوت',self.d)
        self.assertIn('إعادة بناء / تحسين',self.d)
    def test_accessible_readable_tables(self):
        self.assertIn('TargetType="DataGridColumnHeader"',self.style)
        self.assertIn('TargetType="DataGridCell"',self.style)
        self.assertIn('AlternatingRowBackground',self.style)
        self.assertIn('Property="Background" Value="{StaticResource BurgundyBrush}"',self.style)
if __name__=="__main__":unittest.main()
