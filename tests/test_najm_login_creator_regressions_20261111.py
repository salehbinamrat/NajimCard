"""Regression checks for the requested NajmCard login and creator corrections.

These structural checks run before packaging; the Windows WPF build and visual
smoke test verify that the actual controls compile and render correctly.
"""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1] / "src" / "NajmCard.Desktop"
VIEWS = ROOT / "Views"
WPF = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
XNAME = "{http://schemas.microsoft.com/winfx/2006/xaml}Name"

def source(name):
    return (ROOT / name).read_text(encoding="utf-8")

class NajmLoginCreatorFixes(unittest.TestCase):
    def test_login_password_eye_and_compact_connection_history(self):
        root = ET.parse(ROOT / "MainWindow.xaml").getroot()
        self.assertEqual(len(root.findall(f".//{WPF}ToggleButton[@{XNAME}='ShowPasswordInput']")), 1)
        expander = root.find(f".//{WPF}Expander[@{XNAME}='SavedHistoryExpander']")
        self.assertIsNotNone(expander)
        self.assertIsNotNone(expander.find(f".//{WPF}ComboBox[@{XNAME}='SavedConnectionsInput']"))
        self.assertIsNotNone(expander.find(f".//{WPF}DataGrid[@{XNAME}='HistoryConnectionsGrid']"))
        for title in ("حذف", "جديد"):
            self.assertIsNotNone(next(
                (b for b in expander.iter(f"{WPF}Button")
                 if b.get("Content") == title), None))
        self.assertIn('Width="*"', source("MainWindow.xaml"))
        self.assertNotIn("مركز تراخيص حضرموت", source("MainWindow.xaml.cs"))
        self.assertIn('Content="API-SSL"', source("MainWindow.xaml"))
        self.assertIn('Text="حفظ كلمة المرور"', source("MainWindow.xaml"))

    def test_userman_initial_service_and_editable_service_tabs(self):
        code = source("Views/GeneratorView.cs")
        workspace = source("Views/VoucherWorkspaceView.cs")
        self.assertIn("_service.SelectedIndex = 1;", code)
        self.assertIn('_customerPanel.Visibility = SelectedService=="userman"?Visibility.Visible:Visibility.Collapsed;', code)
        self.assertIn('"userman"=>1', code)
        self.assertIn('creator.SelectService(service)', workspace)
        self.assertIn("LastOperationMessage", code)
        self.assertIn("creator.LastOperationMessage", workspace)

    def test_light_dropdown_and_compact_search_fields(self):
        workspace = source("Views/VoucherWorkspaceView.cs")
        theme = source("Themes/BurgundyGold.xaml")
        self.assertIn("ItemsControl.ItemContainerStyleProperty,optionStyle", workspace)
        self.assertIn('ConvertFrom("#EAC67F")', workspace)
        self.assertIn('Background="{TemplateBinding Background}" BorderBrush="{StaticResource GoldBrush}"', theme)
        page_ui = source("Views/PageUi.cs")
        self.assertIn("TextBox SearchInput()", page_ui)
        self.assertIn("Width=170,MinWidth=170,MaxWidth=170,Height=35", page_ui)
        for page in ("CardsView.cs", "ActivityView.cs", "ProfilesView.cs",
                     "ToolsView.cs", "NetworkMaintenanceView.cs",
                     "DashboardView.xaml.cs"):
            with self.subTest(page=page):
                self.assertIn("PageUi.SearchInput()", source("Views/" + page))

    def test_large_hotspot_server_catalog_not_rejected(self):
        actions = source("Views/DashboardView.RouterActions.cs")
        self.assertNotIn("candidates.Length>100", actions)
        self.assertIn("VirtualizingStackPanel.SetIsVirtualizing(selector,true)", actions)
        self.assertIn("ChangeHotspotServerStateAsync(selected,disabled)", actions)

if __name__ == "__main__":
    unittest.main()
