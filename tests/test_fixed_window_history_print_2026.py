"""Regression requirements for the 1200x730 fixed screen and print settings."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
DESKTOP=ROOT/"src"/"NajmCard.Desktop"
V=DESKTOP/"Views"
NS={"w":"http://schemas.microsoft.com/winfx/2006/xaml/presentation"}
XNS="{http://schemas.microsoft.com/winfx/2006/xaml}"

def read(file):return file.read_text(encoding="utf-8")

class FixedScreenPrintAcceptance(unittest.TestCase):
    def test_fixed_1200x730_no_maximize_and_preserve_footer(self):
        window=ET.parse(DESKTOP/"MainWindow.xaml").getroot()
        self.assertEqual(window.attrib["Width"],"1200")
        self.assertEqual(window.attrib["Height"],"730")
        self.assertEqual(window.attrib["ResizeMode"],"NoResize")
        self.assertEqual(window.attrib["MaxWidth"],"1200")
        self.assertEqual(window.attrib["MaxHeight"],"730")
        host=read(V/"VoucherWorkspaceView.cs")
        self.assertIn("DockPanel.SetDock(footer,Dock.Bottom)",host)
        self.assertIn('MakeButton("طباعة الدفعة"',host)

    def test_history_row_click_fills_router_fields_without_connecting(self):
        window=ET.parse(DESKTOP/"MainWindow.xaml").getroot()
        self.assertIsNotNone(window.find(".//w:DataGrid[@"+XNS+"Name='HistoryConnectionsGrid']",NS))
        saved=window.find(".//w:DataGrid[@"+XNS+"Name='HistoryConnectionsGrid']",NS)
        columns=saved.findall("./w:DataGrid.Columns/w:DataGridTextColumn",NS)
        self.assertEqual([c.attrib["Width"] for c in columns],["82","72","120","*"])
        self.assertEqual(saved.attrib["EnableColumnVirtualization"],"False")
        code=read(DESKTOP/"MainWindow.xaml.cs")
        self.assertIn("HistoryGrid_SelectionChanged(",code)
        self.assertIn("SavedConnectionsInput.SelectedItem=item",code)
        for line in ("HostInput.Text=item.Host","UserInput.Text=item.Username",
                     "PasswordInput.Password=item.Password","TlsInput.IsChecked=item.UseTls"):
            self.assertIn(line,code)
        self.assertIn("_savedConnections.Load()",code)
        self.assertIn("SavePasswordInput.IsChecked=SaveConnectionInput.IsChecked==true",code)
        store=read(DESKTOP/"Services"/"SavedRouterConnectionStore.cs")
        self.assertIn("ProtectedData.Protect(",store)
        self.assertIn("DataProtectionScope.CurrentUser",store)

    def test_print_settings_includes_real_controls_without_duplicate_footer(self):
        printer=read(V/"PrintingView.cs")
        selectors=read(V/"PrintingView.PrintSelectors.cs")
        host=read(V/"VoucherWorkspaceView.cs")
        self.assertIn('ReferenceHeader("إعداد الطباعة والدفعة")',printer)
        self.assertIn('"الأعمدة"',printer)
        self.assertIn('"الصفوف"',printer)
        self.assertIn('"الورق"',printer)
        self.assertIn('"الاتجاه"',printer)
        self.assertIn('"الفاصل الرأسي"',printer)
        self.assertIn('"الفاصل الأفقي"',printer)
        self.assertIn('"الهامش"',printer)
        self.assertIn('Field(',printer)
        self.assertIn("PrintDialog()",printer)
        self.assertIn("selectedPrinter",printer)
        self.assertIn("GetPrintQueues",selectors)
        self.assertIn("batchSelector",selectors)
        self.assertIn("selectedBatch()",printer)
        self.assertIn("ExportAdditionalAsync(",host)
        self.assertIn("CheckBox",printer)
        self.assertIn("Excel",printer)
        self.assertIn("CSV",printer)
        self.assertEqual(host.count('MakeButton("طباعة الدفعة"'),1)
        self.assertIn('actions.Visibility=embedded?Visibility.Collapsed:Visibility.Visible',printer)

    def test_batch_export_does_not_include_passwords(self):
        workspace=read(V/"VoucherWorkspaceView.cs")
        self.assertIn("VoucherExcelExporter.Export(",workspace)
        self.assertIn("includePasswords:false",workspace)
        self.assertIn("VoucherBatchCsvExporter.Export(",workspace)

if __name__=="__main__":unittest.main()
