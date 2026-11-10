"""Regression: Hadramout-style package save and preview on NajmCard WPF UI."""
from pathlib import Path
import unittest

VIEW=Path(__file__).resolve().parents[1]/"src"/"NajmCard.Desktop"/"Views"

class HadramoutQuickSaveUi(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.g=(VIEW/"GeneratorView.cs").read_text(encoding="utf-8")
        cls.w=(VIEW/"VoucherWorkspaceView.cs").read_text(encoding="utf-8")

    def test_direct_package_save_uses_existing_sqlite_preset(self):
        s=self.g
        self.assertIn('PageUi.Button("حفظ إعدادات الباقة",false)',s)
        self.assertIn('quickSaveButton.Click+=',s)
        self.assertIn('var saved=SaveCurrentPackage()',s)
        self.assertIn('_store.SavePackagePreset(saved)',s)
        self.assertIn('PrintOptionsProvider?.Invoke()',s)
        self.assertIn('ReadCurrentAppearance()',s)
        self.assertIn('servicePanel.Children.Add(packageSaveFeedback)',s)

    def test_username_only_is_default_even_for_new_unsaved_package(self):
        s=self.g
        self.assertIn('ItemsSource=new[]{"اسم مستخدم فقط","اسم مستخدم وكلمة مرور"},\n        SelectedIndex=0',s)
        self.assertIn('v?.UsernameOnly==false?1:0',s)
        self.assertIn('sameCell.Visibility=differentCell.Visibility=',s)
        self.assertIn('credentialsTop.ColumnDefinitions[1].Width=new GridLength(',s)

    def test_save_feedback_resets_for_service_profile_and_customer(self):
        s=self.g
        self.assertGreaterEqual(s.count('packageSaveFeedback.Visibility=Visibility.Collapsed'),4)
        self.assertIn('if(_sending||_preparing||_restoringPreset)',s)

    def test_failed_preview_does_not_claim_success(self):
        self.assertIn('status.Text=printer.PreparePreview()',self.w)
        self.assertIn('لم تُجهز معاينة الطباعة',self.w)
        self.assertNotIn('printer.PreparePreview();\n                status.Text="معاينة الكروت',self.w)

if __name__=="__main__":
    unittest.main()
