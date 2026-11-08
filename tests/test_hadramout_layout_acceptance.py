"""Acceptance contract for NajmCard 1.1.3 visual parity.
The source reference is the customer's attached Hadramout-style screen.
Tests are deliberately red on the previous broken-geometry branch."""
import re
import unittest
from pathlib import Path
R=Path(__file__).resolve().parents[1]/"src/NajmCard.Desktop"
class HadramoutLayoutAcceptance(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.g=(R/"Views/GeneratorView.cs").read_text(encoding="utf-8")
        cls.layout=(R/"Views/GeneratorView.ReferenceLayout.cs").read_text(encoding="utf-8")
        cls.creator=(R/"Views/VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        cls.design=(R/"Views/GeneratorView.CardDesign.cs").read_text(encoding="utf-8")
        cls.editor=(R/"Views/GeneratorView.DesignerPanel.cs").read_text(encoding="utf-8")
        cls.dash=(R/"Views/DashboardView.xaml").read_text(encoding="utf-8")
        cls.dashcode=(R/"Views/DashboardView.xaml.cs").read_text(encoding="utf-8")
        cls.theme=(R/"Themes/BurgundyGold.xaml").read_text(encoding="utf-8")
    def test_two_exactly_aligned_columns_without_side_gutters(self):
        self.assertIn("CreateReferenceLayout(", self.g)
        self.assertNotIn("left.Margin=new Thickness(0,0,9,0)",self.g)
        self.assertIn("Grid.SetRow(routeSurface,0)",self.layout)
        self.assertIn("Grid.SetRow(formatSurface,0)",self.layout)
        self.assertIn("Grid.SetRow(designSurface,1)",self.layout)
        self.assertIn("Grid.SetRow(secondary,1)",self.layout)
        self.assertIn("Width=new GridLength(48,GridUnitType.Star)",self.layout)
        self.assertIn("Width=new GridLength(52,GridUnitType.Star)",self.layout)
        self.assertNotIn("Viewbox",self.creator)
    def test_cropped_in_place_editor_controls_are_moved_to_modal(self):
        self.assertIn("تعديلات متقدمة",self.editor)
        self.assertIn("ChooseTemplateImage()",self.editor)
        self.assertIn("WindowStartupLocation.CenterOwner",self.editor)
        self.assertNotIn("IsExpanded=true",self.editor)
        self.assertIn("BuildSampleVoucherVisual()",self.g)
        self.assertIn("TemplateImagePath",self.design)
        self.assertIn("SaveCurrentPackage()",self.g)
    def test_username_mode_persisted_and_functionally_used(self):
        self.assertIn("_credentialMode.SelectedIndex==0",self.g)
        self.assertIn("UsernameOnly",self.g)
        self.assertIn("_credentialsSame",self.g)
        self.assertIn("_credentialsDifferent",self.g)
    def test_password_and_username_character_sets_have_real_dropdowns(self):
        for field in ("_usernameAlphabetType","_passwordAlphabetType"):
            self.assertIn("private readonly ComboBox "+field,self.g)
            self.assertIn(field+".SelectionChanged",self.g)
        for option in ("أرقام فقط","أحرف فقط","مختلطة","مخصصة"):
            self.assertIn(option,self.g)
        self.assertIn("VoucherGenerator.UppercaseLetters",self.g)
        self.assertIn("VoucherGenerator.Mixed",self.g)

    def test_dashboard_is_table_not_circle(self):
        self.assertNotIn("StatusRing",self.dash)
        self.assertNotIn("StatusRing",self.dashcode)
        for name in ("ActivePercent","ExpiredPercent","ValidPercent",
                     "ActiveLegend","ExpiredLegend","DisabledLegend"):
            self.assertIn(name,self.dash)
        self.assertIn("عرض النشطة",self.dash)
        self.assertIn("عرض المنتهية",self.dash)
    def test_dashboard_balanced_cards_and_buttons(self):
        self.assertIn('Text="●  متصل"',self.dash)
        self.assertIn("تعطيل الهوتسبوت",self.dash)
        self.assertIn("إعادة بناء / تحسين",self.dash)
        self.assertIn('Text="الرئيسية"',self.dash)
        self.assertIn("ProfileBars",self.dash)
        self.assertIn("ActivityRows",self.dash)
    def test_tables_use_readable_dark_headers_and_contrasting_body(self):
        self.assertIn('Value="#142C47"',self.theme)
        self.assertIn('Value="#142C47"',self.theme)
        self.assertIn('Value="#FFFFFF"',self.theme)
        self.assertIn('TargetType="DataGridCell"',self.theme)
    def test_no_extra_workspace_title_or_scrollbar(self):
        self.assertIn('ClipToBounds=true',self.creator)
        self.assertIn("VerticalScrollBarVisibility=ScrollBarVisibility.Auto",self.creator)
        self.assertIn("HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled",self.creator)
        self.assertIn("Columns=6",self.creator)
if __name__=="__main__":unittest.main()
