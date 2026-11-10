"""Owner regression: dashboard once-per-login snapshot, 1s live telemetry, card parity."""
import pathlib
import unittest

ROOT=pathlib.Path(__file__).resolve().parents[1]
DESKTOP=ROOT/"src/NajmCard.Desktop"
V=DESKTOP/"Views"
C=ROOT/"src/NajmCard.Core/Services"
def read(path): return path.read_text(encoding="utf-8")

class NewDashboardAndCreatorContract(unittest.TestCase):
    def test_snapshot_once_per_router_session(self):
        main=read(DESKTOP/"MainWindow.xaml.cs")
        dash=read(V/"DashboardView.xaml.cs")
        self.assertIn("_dashboardPage",main)
        self.assertIn("_dashboardPage ??=",main)
        self.assertIn("_dashboardPage=null",main)
        self.assertIn("_initialSnapshotLoaded",dash)
        self.assertIn("ScheduleInitialLifecycleAsync()",dash)
        self.assertIn("RefreshLifecycleAsync()",dash)
        self.assertIn("Lifecycle_Click",dash)

    def test_telemetry_is_one_second_and_does_not_refresh_total(self):
        dash=read(V/"DashboardView.xaml.cs")
        gateway=read(C/"RouterGateway.DashboardLiveCounts.cs")
        self.assertIn("TimeSpan.FromSeconds(1)",dash)
        self.assertIn("ReadDashboardOnlineOnlyAsync(",dash)
        self.assertIn("ReadDashboardOnlineOnlyAsync(",gateway)
        self.assertIn("RefreshLiveTelemetryAsync(",dash)
        self.assertNotIn("AllUsers.Text=",dash.split("private async Task RefreshLiveTelemetryAsync(")[-1].split("private async Task")[0])

    def test_sidebar_trial_countdown_and_logout(self):
        ui=read(DESKTOP/"MainWindow.xaml")
        logic=read(DESKTOP/"MainWindow.xaml.cs")
        self.assertIn('x:Name="SidebarLicense"',ui)
        self.assertNotIn('x:Name="SidebarRouter"',ui)
        self.assertIn('Content="تسجيل الخروج"',ui)
        self.assertIn('Background="#790D20"',ui)
        self.assertIn("RemainingSeconds",logic)
        self.assertIn("UpdateLicenseCountdown",logic)

    def test_cream_and_burgundy_with_distinct_actions(self):
        ui=read(V/"DashboardView.xaml")
        self.assertIn('Color="#FFF3DF"',ui)
        self.assertIn('Color="#72203A"',ui)
        self.assertIn('Content="عرض النشطة"',ui)
        self.assertIn('Content="عرض المنتهية"',ui)
        self.assertIn('#197A54',ui)
        self.assertIn('#96243F',ui)
        dashboard=read(V/"DashboardView.xaml.cs")
        self.assertIn("palette[i%palette.Length]",dashboard)

    def test_preview_and_pdf_share_numeric_plate(self):
        preview=read(V/"GeneratorView.CardDesign.cs")
        printing=read(V/"PrintingView.cs")
        helper=read(V/"CardCredentialPlate.cs")
        self.assertIn("CardCredentialPlate.Build(",preview)
        self.assertIn("CardCredentialPlate.Build(",printing)
        self.assertIn("Stretch=Stretch.Uniform",helper)
        self.assertIn("CardWidthMm",preview)
        self.assertIn("CardHeightMm",preview)

    def test_all_numeric_options_except_batch_and_pages_are_dropdowns(self):
        generator=read(V/"GeneratorView.cs")
        printer=read(V/"PrintingView.cs")
        self.assertIn("CompactNumberChoice(",generator)
        self.assertIn('("عدد الكروت", _count)',generator)
        self.assertIn('("عدد الصفحات", _pagesEstimate)',generator)
        self.assertIn("CompactNumberChoice(",printer)

    def test_card_element_checkbox_titles_remain_visible(self):
        designer=read(V/"GeneratorView.DesignerPanel.cs")
        self.assertIn("var caption=new TextBlock",designer)
        self.assertIn("caption.MouseLeftButtonUp+=",designer)
        self.assertIn("option.Children.Add(caption)",designer)

    def test_one_line_route_and_side_by_side_username_kind_and_chars(self):
        generator=read(V/"GeneratorView.cs")
        self.assertIn("servicePanel.Children.Add(_customerPanel)",generator)
        self.assertIn("usernameGroup.Children.Add(userKind)",generator)
        self.assertIn("alphabetFields.Children.Add(usernameCharsCell)",generator)
        self.assertIn("quickSaveButton.MinHeight=34",generator)

if __name__=="__main__": unittest.main()
