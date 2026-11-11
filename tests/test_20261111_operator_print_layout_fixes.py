"""Regression contract for the operator's 2026-11-11 layout and issuing fixes.

The CI Windows Core job also compiles and captures the real WPF UI.
"""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]/"src"/"NajmCard.Desktop"
V=ROOT/"Views"
WPF="{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
XNAME="{http://schemas.microsoft.com/winfx/2006/xaml}Name"
def read(p): return p.read_text(encoding="utf-8")

class OperatorCorrections(unittest.TestCase):
    def test_eye_on_physical_right_of_password_without_overlay(self):
        tree=ET.parse(ROOT/"MainWindow.xaml").getroot()
        eye=tree.find(f".//{WPF}ToggleButton[@{XNAME}='ShowPasswordInput']")
        self.assertIsNotNone(eye)
        self.assertEqual(eye.get("HorizontalAlignment"),"Right")
        field=tree.find(f".//{WPF}PasswordBox[@{XNAME}='PasswordInput']")
        self.assertEqual(field.get("Padding"),"8,6,44,6")
        self.assertIn('<Grid FlowDirection="LeftToRight">',read(ROOT/"MainWindow.xaml"))

    def test_exact_six_actions_right_to_left(self):
        workspace=read(V/"VoucherWorkspaceView.cs")
        names=[
            "إنشاء وإرسال وحفظ",
            "إنشاء وإرسال فقط",
            "معاينة قبل الإنشاء",
            "ملف PDF",
            "معاينة PDF",
            "طباعة الدفعة",
        ]
        positions=[workspace.index('MakeButton("'+name+'"') for name in names]
        self.assertEqual(positions,sorted(positions))
        self.assertIn('actions[0].Click+=async',workspace)
        self.assertIn('actions[1].Click+=async',workspace)
        self.assertIn('creator.CreateAndSendAsync(saveBeforeSend:',workspace)
        self.assertIn('creator.CreateAndSendAsync();',workspace)
        self.assertNotIn("singleCard:true",workspace)
        self.assertIn("DockPanel.SetDock(footer,Dock.Bottom)",workspace)

    def test_package_save_in_header_preserves_all_settings(self):
        creator=read(V/"GeneratorView.cs")
        workspace=read(V/"VoucherWorkspaceView.cs")
        self.assertIn('PackageSaveButton=quickSaveButton',creator)
        self.assertIn('if(!embedded)routeHeader.Children.Add(quickSaveButton)',creator)
        self.assertIn('var pageSave=creator.PackageSaveButton',workspace)
        self.assertIn('titleGrid.Children.Add(pageSave)',workspace)
        self.assertIn('titleGrid.Children.Add(titleLabel)',workspace)
        for key in ('ReadCurrentAppearance()','PrintOptionsProvider?.Invoke()','_store.SavePackagePreset(saved)'):
            self.assertIn(key,creator)
        start=creator.index('public async Task<bool> CreateAndSendAsync(')
        end=creator.index('private void SelectMatchingOwner()',start)
        self.assertNotIn("SaveCurrentPackage()",creator[start:end])
        self.assertIn("await PrepareBatchAsync(true,singleCard)",creator[start:end])
        self.assertIn('var saved=SaveCurrentPackage()',read(V/"GeneratorView.SaveToast.cs"))

    def test_diagnostics_are_visible_without_relaxing_router_checks(self):
        creator=read(V/"GeneratorView.cs")
        workspace=read(V/"VoucherWorkspaceView.cs")
        store=read(ROOT/"Services"/"VoucherStore.DeliveryHistory.cs")
        printer=read(V/"PrintingView.cs")
        self.assertIn("_store.FirstDeliveryError(service,current[0].BatchId)",creator)
        self.assertIn("public string? FirstDeliveryError(",store)
        self.assertIn("public string LastOperationMessage => _status.Text",printer)
        self.assertIn("status.Text=printer.LastOperationMessage",workspace)
        self.assertIn("confirmed",printer.lower())
        self.assertIn('x.DeliveryStatus=="SENT"&&x.SentToRouter',printer)
        self.assertIn('if(saved)_status.Text=""',read(V/"GeneratorView.SaveToast.cs"))

    def test_readable_dropdowns_and_compact_search(self):
        page=read(V/"PageUi.cs")
        theme=read(ROOT/"Themes"/"BurgundyGold.xaml")
        workspace=read(V/"VoucherWorkspaceView.cs")
        self.assertIn("Width=150,MinWidth=150,MaxWidth=150,Height=32",page)
        self.assertIn('TextElement.Foreground="{TemplateBinding Foreground}"',theme)
        self.assertIn('Value="#EAC67F"',theme)
        self.assertIn('ItemsControl.ItemContainerStyleProperty,optionStyle',workspace)

    def test_dashboard_rebalances_unique_actions(self):
        ui=read(V/"DashboardView.xaml")
        self.assertNotIn('Text="إجراءات سريعة"',ui)
        self.assertNotIn('Click="NewCards_Click"',ui)
        self.assertNotIn('Click="ManageCards_Click"',ui)
        self.assertNotIn('Click="Reports_Click"',ui)
        self.assertIn('Columns="3" Rows="1"',ui)
        for action in ("DisableHotspot_Click","RestoreHotspot_Click","RebootRouter_Click"):
            self.assertIn('Click="'+action+'"',ui)
        self.assertIn('x:Name="RouterName"',ui)
        self.assertIn('x:Name="RefreshHistory"',ui)

if __name__=="__main__":
    unittest.main()
