"""Hadramout photographed management screens: structural acceptance checks.
Complement actual RouterOS, SQLite and Windows GUI tests; not a visual screenshot claim.
"""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
V=ROOT/"src"/"NajmCard.Desktop"/"Views"

class HadramoutManagementScreens(unittest.TestCase):
    def test_network_packages_have_three_services_with_real_profile_data(self):
        s=(V/"ProfilesView.cs").read_text(encoding="utf-8")
        for label in ("هوتسبوت", "مدير المستخدمين", "برودباند", "ReadUserManagerV6ProfilesAsync",
                      "ReadUserManagerV6ProfileLimitSummaryAsync", "ReadPppInventoryAsync",
                      "TabControl","البحث","إجراءات الباقة"):
            self.assertIn(label,s)

    def test_directory_has_service_tabs_search_and_pages(self):
        s=(V/"CardsView.cs").read_text(encoding="utf-8")
        for label in ("TabControl","هوتسبوت","مدير المستخدمين","برودباند",
                      "بحث","كل الباقات","السابق","التالي","ReadSessionsAsync"):
            self.assertIn(label,s)

    def test_network_maintenance_has_four_source_tab_names_and_real_reads(self):
        s=(V/"NetworkMaintenanceView.cs").read_text(encoding="utf-8")
        for label in ("الجلسات","الأجهزة المتصلة","قاعدة البيانات والراوتر",
                      "سيرفرات الهوتسبوت","ReadNearbyDevicesAsync",
                      "ReadHotspotServerStatusAsync","ReadUserManagerV6DatabaseStatusAsync"):
            self.assertIn(label,s)
        main=(ROOT/"src"/"NajmCard.Desktop"/"MainWindow.xaml.cs").read_text(encoding="utf-8")
        self.assertIn('"tools" => new NetworkMaintenanceView(',main)

    def test_activity_is_standalone_filtered_local_sqlite_log(self):
        s=(V/"ActivityView.cs").read_text(encoding="utf-8")
        for label in ("ReadActivityPage", "الفئة","البحث","السابق","التالي"):
            self.assertIn(label,s)
        main=(ROOT/"src"/"NajmCard.Desktop"/"MainWindow.xaml.cs").read_text(encoding="utf-8")
        self.assertIn('"activity" => new ActivityView(',main)

    def test_lifecycle_details_show_transfer_usage_and_page_dont_fake_expiry(self):
        s=(V/"DashboardView.xaml.cs").read_text(encoding="utf-8")
        for label in ("RemainingLabel","LastSeen","UptimeUsed","السابق","التالي"):
            self.assertIn(label,s)

if __name__=="__main__":unittest.main()
