"""Regression guards for digit rendering, Arial and single-screen titles.
Live WPF compilation and RouterGateway tests remain mandatory.
"""
from pathlib import Path
import unittest,re
ROOT=Path(__file__).resolve().parents[1]
DESKTOP=ROOT/"src"/"NajmCard.Desktop"
class TypographyAndDeletionAcceptance(unittest.TestCase):
    def test_all_ui_uses_arial_and_ascii_digits(self):
        theme=(DESKTOP/"Themes/BurgundyGold.xaml").read_text(encoding="utf-8")
        app=(DESKTOP/"App.xaml.cs").read_text(encoding="utf-8")
        shared=(DESKTOP/"Views/PageUi.cs").read_text(encoding="utf-8")
        self.assertIn('FontFamily" Value="Arial"',theme)
        self.assertIn('FrameworkElement.LanguageProperty.OverrideMetadata',app)
        self.assertIn('CultureInfo.GetCultureInfo("en-US")',app)
        self.assertIn("NormalizeAsciiDigits",app)
        self.assertNotIn('new FontFamily("Segoe UI")',shared)
        for name in ("MainWindow.xaml","Views/DashboardView.xaml"):
            text=(DESKTOP/name).read_text(encoding="utf-8")
            self.assertIsNone(re.search(r"[\u0660-\u0669\u06f0-\u06f9]",text),name)
    def test_no_duplicate_page_title_beneath_main_title(self):
        ui=(DESKTOP/"Views/PageUi.cs").read_text(encoding="utf-8")
        self.assertNotIn("stack.Children.Add(Title(title))",ui)
        self.assertNotIn("stack.Children.Add(Hint(subtitle))",ui)
        for p,header in [
            ("ProfilesView.cs",'main.Children.Add(PageUi.Title("باقات الشبكة"))'),
            ("NetworkMaintenanceView.cs",'root.Children.Add(PageUi.Title("التحكم بالشبكة والصيانة"))'),
            ("ActivityView.cs",'root.Children.Add(PageUi.Title("النشاط وسجل العمليات"))'),
            ("VoucherWorkspaceView.cs",'fields.Children.Add(heading)')]:
            self.assertNotIn(header,(DESKTOP/"Views"/p).read_text(encoding="utf-8"))
    def test_dashboard_expired_popup_has_safe_fast_delete_and_not_global_remove(self):
        s=(DESKTOP/"Views/DashboardView.xaml.cs").read_text(encoding="utf-8")
        for label in ("حذف الكروت المنتهية","DeleteExpiredUserManagerV6Async(",
                      "RemainingUserManager","ExpiredTransferDeletionProgress",
                      "PageUi.Confirm(","UncertainOutcome"):
            self.assertIn(label,s)
        self.assertNotIn("await RefreshLifecycleAsync();",s[s.index("private void OpenLifecycleDetails_Click"):s.index("private void RenderActivity")])
    def test_tables_have_consistent_geometric_and_legible_style(self):
        s=(DESKTOP/"Themes/BurgundyGold.xaml").read_text(encoding="utf-8")
        self.assertIn("DataGridColumnHeader",s)
        self.assertIn("AlternatingRowBackground",s)
        self.assertIn("FontFamily",s)
if __name__=="__main__":unittest.main()
