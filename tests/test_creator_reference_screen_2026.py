"""Page-only Hadramout reference layout contract for NajmCard.

This regression suite deliberately checks the creator workspace, rather than
MainWindow/sidebar, because navigation must remain unchanged.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src/NajmCard.Desktop/Views"

class CreatorReferenceScreenTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.creator = (ROOT / "GeneratorView.cs").read_text(encoding="utf-8")
        cls.layout = (ROOT / "GeneratorView.ReferenceLayout.cs").read_text(encoding="utf-8")
        cls.printer = (ROOT / "PrintingView.cs").read_text(encoding="utf-8")
        cls.workspace = (ROOT / "VoucherWorkspaceView.cs").read_text(encoding="utf-8")

    def test_upper_left_contains_credentials_and_generation_inputs(self):
        self.assertIn('ReferenceHeader("إنشاء المستخدم وطباعة الكروت")', self.creator)
        self.assertIn('basicsSection.Children.Add(row);', self.creator)
        self.assertIn('Grid.SetRow(formatSurface,1)', self.layout)
        self.assertIn('printColumn.Children.Add(basicsSurface)', self.layout)

    def test_upper_right_preserves_profile_and_package_save(self):
        self.assertIn('ReferenceHeader("بيانات ومسار الإرسال")', self.creator)
        self.assertIn('var quickSaveButton=PageUi.Button("حفظ إعدادات الباقة",false)', self.creator)
        self.assertIn('routeInputs.Children.Add(_customerPanel)', self.creator)
        self.assertIn('Grid.SetRow(routeSurface,0)', self.layout)

    def test_lower_left_is_two_printing_settings_groups(self):
        self.assertIn('ReferenceHeader("إعداد الطباعة والدفعة")', self.printer)
        self.assertIn('Field(ordering,"الطابعة",selectedPrinter)', self.printer)
        self.assertIn('var basicPrintFields=new UniformGrid{Columns=4', self.printer)
        self.assertIn('var pagePrintFields=new UniformGrid{Columns=5', self.printer)
        self.assertIn('_embeddedPrintHost', self.layout)

    def test_lower_right_live_designer_and_footer_actions(self):
        self.assertIn('Grid.SetColumn(designSurface,1)', self.layout)
        self.assertIn('BuildSampleVoucherVisual()', self.creator)
        for label in ("إنشاء وإرسال", "إعادة الإعدادات", "معاينة قبل الطباعة",
                      "حفظ PDF", "طباعة الدفعة"):
            self.assertIn(label, self.workspace)
        self.assertIn('creator.RestoreSavedPackageSettings()', self.workspace)
        self.assertIn('buttonsGrid=new UniformGrid{Columns=7', self.workspace)

    def test_workspace_styles_are_local_and_do_not_touch_navigation(self):
        self.assertIn('root.Resources[typeof(ComboBox)]', self.workspace)
        self.assertIn('root.Resources[typeof(TextBox)]', self.workspace)
        self.assertIn('Content=root;', self.workspace)
        self.assertNotIn('new MainWindow(', self.workspace)

if __name__ == "__main__":
    unittest.main()
