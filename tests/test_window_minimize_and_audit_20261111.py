"""Regression gates for a fixed-size taskbar-minimizable NajmCard shell.

Complements the Windows-only visual smoke and .NET/DPAPI/SQLite tests:
this checks UI event wiring, serialized background reads and safe teardown.
"""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
DESKTOP=ROOT/"src/NajmCard.Desktop"
VIEWS=DESKTOP/"Views"


def source(path):
    return path.read_text(encoding="utf-8")


class TaskbarAndAuditRegressions(unittest.TestCase):
    def test_native_taskbar_minimize_without_maximize_or_resize(self):
        window=ET.parse(DESKTOP/"MainWindow.xaml").getroot()
        self.assertEqual(window.attrib["ResizeMode"],"CanMinimize")
        self.assertEqual(window.attrib["ShowInTaskbar"],"True")
        self.assertEqual(window.attrib["Width"],window.attrib["MinWidth"])
        self.assertEqual(window.attrib["Width"],window.attrib["MaxWidth"])
        self.assertEqual(window.attrib["Height"],window.attrib["MinHeight"])
        self.assertEqual(window.attrib["Height"],window.attrib["MaxHeight"])
        self.assertEqual(window.attrib["Width"],"1200")
        self.assertEqual(window.attrib["Height"],"730")
        app=ET.parse(DESKTOP/"App.xaml").getroot()
        self.assertEqual(app.attrib["ShutdownMode"],"OnMainWindowClose")
        smoke=source(ROOT/"tests/NajmCard.VisualSmoke/Program.cs")
        self.assertIn("window.ResizeMode!=ResizeMode.CanMinimize",smoke)
        self.assertIn("!window.ShowInTaskbar",smoke)

    def test_no_router_polling_in_minimized_dashboard(self):
        dashboard=source(VIEWS/"DashboardView.xaml.cs")
        self.assertIn("Window.GetWindow(this)?.WindowState==WindowState.Minimized",dashboard)
        self.assertIn("_telemetryTimer.Tick+=async",dashboard)
        self.assertIn("_telemetryTimer.Start()",dashboard)
        self.assertIn("_telemetryTimer.Stop()",dashboard)

    def test_activity_filter_requests_are_not_dropped_or_stale(self):
        activity=source(VIEWS/"ActivityView.cs")
        self.assertIn("if(busy){reloadPending=true;return;}",activity)
        self.assertIn("do\n",activity)
        self.assertIn("}while(reloadPending);",activity)
        self.assertGreaterEqual(activity.count("if(reloadPending)continue;"),2)
        self.assertIn("var searchTerm=search.Text;",activity)
        self.assertIn("var requestedPage=current;",activity)
        self.assertIn("store.ReadActivityPage(key,searchTerm,requestedPage,250)",activity)
        self.assertIn("previous.IsEnabled=next.IsEnabled=false",activity)

    def test_session_and_server_pages_do_not_overlap_refresh(self):
        sessions=source(VIEWS/"SessionsView.cs")
        servers=source(VIEWS/"ServersView.cs")
        for text in [sessions,servers]:
            self.assertIn("if(busy)return;",text)
            self.assertIn("refresh.IsEnabled=false;",text)
            self.assertIn("Loaded+=async (_,_)=>await Load();",text)
        self.assertIn("grid.ItemsSource=null;",sessions)

    def test_dialog_close_and_shutdown_cleanup_are_explicit(self):
        history=source(VIEWS/"VoucherDeliveryHistoryWindow.cs")
        main=source(DESKTOP/"MainWindow.xaml.cs")
        self.assertIn("close.Click += (_, _) => Close();",history)
        self.assertIn("try{await previous.DisposeAsync();}",main)
        self.assertIn("try{await pending;}",main)
        self.assertIn("System.Diagnostics.Trace.TraceWarning(",main)

    def test_backup_names_are_unique_not_second_precision_overwrites(self):
        store=source(DESKTOP/"Services/VoucherStore.cs")
        self.assertIn('DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"',store)
        self.assertIn('Guid.NewGuid().ToString("N")[..8]',store)
        native=source(ROOT/"tests/NajmCard.Desktop.Tests/VoucherStoreServiceTests.cs")
        self.assertIn("Repeated_Backups_Do_Not_Overwrite_One_Another",native)
        self.assertIn("snapshot.Open()",native)

if __name__=="__main__":
    unittest.main()
