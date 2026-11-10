"""Regression checks for Hadramout parity of the consolidated voucher workspace.

These complement the actual RouterOS/SQLite/printing integration tests;
they do not by themselves prove end-user WPF visual or MikroTik parity.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
VIEWS = ROOT / "src" / "NajmCard.Desktop" / "Views"


class HadramoutWorkspaceContract(unittest.TestCase):
    def test_workflow_section_order_matches_hadramout(self):
        generator=(VIEWS/"GeneratorView.cs").read_text(encoding="utf-8")
        designer=(VIEWS/"GeneratorView.DesignerPanel.cs").read_text(encoding="utf-8")
        layout=(VIEWS/"GeneratorView.ReferenceLayout.cs").read_text(encoding="utf-8")
        self.assertIn('ReferenceHeader("بيانات ومسار الإرسال")',generator)
        self.assertIn('ReferenceHeader("إنشاء المستخدم وطباعة الكروت")',generator)
        self.assertIn('ReferenceHeader("معاينة وتصميم الكرت")',designer)
        self.assertIn('CreateReferenceLayout(',generator)
        self.assertIn('Grid.SetRow(routeSurface,0)',layout)
        self.assertIn('Grid.SetRow(formatSurface,0)',layout)
        self.assertIn('Grid.SetRow(designSurface,1)',layout)
        self.assertIn('_designPreviewRow.Children.Insert(0,scalableCard)',generator)
        self.assertIn('Child=_appearancePreviewBorder',generator)

    def test_dashboard_fetches_lifecycle_automatically_after_login_without_repeat_scans(self):
        source = (VIEWS / "DashboardView.xaml.cs").read_text(encoding="utf-8")
        self.assertIn('Task.Delay(TimeSpan.FromSeconds(5)', source)
        self.assertIn('await RefreshLifecycleAsync()', source)
        self.assertIn('ReadCachedUserManagerV6InventoryAsync(false', source)
        self.assertIn('_initialLifecycleToken?.Cancel()', source)
        self.assertIn('_lifecycleReading', source)

    def test_editor_matches_hadramout_two_column_topology(self):
        generator=(VIEWS/"GeneratorView.cs").read_text(encoding="utf-8")
        layout=(VIEWS/"GeneratorView.ReferenceLayout.cs").read_text(encoding="utf-8")
        workspace=(VIEWS/"VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        self.assertIn('CreateReferenceLayout(',generator)
        self.assertIn('var split=new Grid',layout)
        self.assertIn('Grid.SetColumn(formatSurface,0)',layout)
        self.assertIn('Grid.SetColumn(routeSurface,1)',layout)
        self.assertIn('secondary.Children.Add(_embeddedPrintHost)',layout)
        self.assertIn('creator.AttachPrintingSection(printer)',workspace)
        self.assertNotIn('fields.Children.Add(printer)',workspace)

    def test_last_activity_is_real_local_audit_log_not_polling_routeros_history(self):
        source = (VIEWS / "DashboardView.xaml.cs").read_text(encoding="utf-8")
        self.assertIn('_store.ReadRecentActivities(6)', source)
        self.assertIn('RenderActivity(activities)', source)


if __name__ == "__main__":
    unittest.main()
