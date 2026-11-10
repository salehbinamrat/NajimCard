"""Owner acceptance checks: NajmCard visual fit, color contrast and reference alignment.

These deliberately protect actual UI source structure and palette; Windows CI also
builds WPF and runs Core/Desktop functional tests before any four installers.
"""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1] / "src" / "NajmCard.Desktop"
VIEWS = ROOT / "Views"

def source(name):
    return (VIEWS / name).read_text(encoding="utf-8")

class OwnerVisualAcceptance(unittest.TestCase):
    def test_card_editor_matches_two_column_reference_order(self):
        layout = source("GeneratorView.ReferenceLayout.cs")
        self.assertIn("Grid.SetColumn(routeSurface,1)", layout)
        self.assertIn("full.Children.Add(formatSurface)", layout)
        self.assertIn("Grid.SetColumn(designSurface,1)", layout)
        self.assertIn("Grid.SetRow(printColumn,1)", layout)
        generator = source("GeneratorView.cs")
        for title in ("باقة وسيرفر الإرسال", "اسم المستخدم وكلمة المرور",
                      "عدد الكروت"):
            self.assertIn(title, generator)

    def test_card_workspace_is_burgundy_gold_and_fields_are_legible(self):
        workspace = source("VoucherWorkspaceView.cs")
        designer = source("GeneratorView.DesignerPanel.cs")
        card_style = source("GeneratorView.CardDesign.cs")
        printing = source("PrintingView.cs")
        self.assertNotIn("#F8EFD9", workspace + designer + card_style + printing)
        self.assertNotIn("WhiteDropdownStyle", workspace)
        self.assertIn('FindResource("DarkComboBox")', workspace)
        self.assertIn('root.Resources[typeof(TextBox)]', workspace)
        self.assertIn('ReferenceHeader("معاينة وتصميم الكرت")', designer)
        self.assertIn("BuildSampleVoucherVisual()", source("GeneratorView.cs"))
        self.assertIn("TemplateImagePath", card_style)

    def test_dashboard_has_equal_stats_and_no_circle_in_lifecycle(self):
        raw = source("DashboardView.xaml")
        tree = ET.fromstring(raw)
        self.assertIsNotNone(tree)
        ns = {"w": "http://schemas.microsoft.com/winfx/2006/xaml/presentation"}
        layout = tree.find("w:Grid", ns)
        tiers = layout.findall("w:Grid", ns)
        self.assertEqual(3, len(tiers))
        for tier in tiers:
            columns = tier.findall("w:Grid.ColumnDefinitions/w:ColumnDefinition", ns)
            self.assertEqual(["*", "8", "*", "8", "*"], [c.attrib["Width"] for c in columns])
        self.assertNotIn("StatusRing", raw)
        self.assertNotIn('Text="● نشطة"', raw)
        self.assertNotIn('Text="● منتهية"', raw)
        self.assertNotIn('Text="● صالحة"', raw)
        self.assertIn('<UniformGrid Columns="3">', raw)
        self.assertIn("عرض النشطة", raw)
        self.assertIn("عرض المنتهية", raw)

    def test_advanced_editor_edits_real_print_geometry_and_card_style(self):
        editor = source("GeneratorView.DesignerPanel.cs")
        generator = source("GeneratorView.cs")
        workspace = source("VoucherWorkspaceView.cs")
        for label in ("عرض الكرت (مم)", "ارتفاع الكرت (مم)",
                      "نوع الخط", "إظهار كلمة المرور", "إظهار الباقة"):
            self.assertIn(label, editor)
        self.assertIn("ChooseTemplateImage()", editor)
        self.assertIn("CardPrintLayoutPlanner.Calculate", editor)
        self.assertIn("ApplyPrintOptions?.Invoke", editor)
        self.assertIn("public Action<CardPrintOptions>? ApplyPrintOptions", generator)
        self.assertIn("creator.ApplyPrintOptions=printer.ApplyOptions", workspace)

    def test_artwork_uses_contrasting_ink_backing_and_rejects_broken_images(self):
        printing = source("PrintingView.cs")
        image_picker = source("GeneratorView.ReferenceLayout.cs")
        preview = source("GeneratorView.CardDesign.cs")
        # The former hard-coded cream backing made user-selected white ink
        # illegible on any light image. Match backing tone to chosen ink.
        self.assertIn("ChooseArtworkTextBacking", printing)
        self.assertIn("Color.FromArgb(225,28,12,29)", printing)
        self.assertIn("Color.FromArgb(225,255,250,237)", printing)
        self.assertIn("FormatException", image_picker)
        self.assertIn("UnauthorizedAccessException", image_picker)
        self.assertIn("FormatException", preview)
        self.assertIn("appearance.EffectiveElementColor", preview)
        self.assertNotIn("Color.FromArgb(230,255,255,255)", preview)

    def test_cancelled_advanced_editor_does_not_change_active_card_style(self):
        editor = source("GeneratorView.DesignerPanel.cs")
        self.assertIn("popup.Closed+=", editor)
        self.assertIn("originalAppearance", editor)
        self.assertIn("savedSuccessfully", editor)
        self.assertIn("ApplyCardAppearance(originalAppearance)", editor)

    def test_release_workflow_does_not_duplicate_feature_push_and_pr_runs(self):
        workflow = (ROOT.parents[1] / ".github" / "workflows" / "windows-ci.yml").read_text(encoding="utf-8")
        self.assertIn("branches: ['main']", workflow)
        self.assertIn("branches: ['main', 'ui/hadramout-layout-match']", workflow)
        self.assertIn("concurrency:", workflow)
        self.assertIn("cancel-in-progress: true", workflow)
        self.assertIn("needs: [windows-core, windows-release]", workflow)

    def test_shared_tables_have_opaque_contrasting_headers_and_rows(self):
        theme = (ROOT / "Themes" / "BurgundyGold.xaml").read_text(encoding="utf-8")
        ET.fromstring(theme)
        self.assertIn('TargetType="DataGridColumnHeader"', theme)
        self.assertIn('Property="Foreground" Value="{StaticResource TextBrush}"', theme)
        self.assertIn('Property="RowBackground" Value="#FFF8EC"', theme)
        self.assertIn('TargetType="DataGridRow"', theme)

if __name__ == "__main__":
    unittest.main()
