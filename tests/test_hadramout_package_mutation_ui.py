"""UI regression: real Hotspot CRUD must be reachable from the unified packages view.
Structural checks complement, but cannot replace, the RouterGateway and Windows UI tests.
"""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
VIEW=ROOT/"src"/"NajmCard.Desktop"/"Views"/"ProfilesView.cs"

class PackageMutationContract(unittest.TestCase):
    def test_hotspot_actions_wired_and_ppp_existing_rate_editor_integrated(self):
        src=VIEW.read_text(encoding="utf-8")
        for symbol in ("SaveHotspotProfileAsync(", "DeleteHotspotProfileAsync(",
                       "SetPppProfileRateLimitAsync(", "إضافة / حفظ على الراوتر",
                       "حذف الباقة المحددة", "PageUi.Confirm("):
            with self.subTest(symbol=symbol):
                self.assertIn(symbol,src)
        self.assertIn("RouterCardProfile? Hotspot",src)
        self.assertIn("PppProfile? Ppp",src)
        self.assertIn("saved.UncertainOutcome",src)
        self.assertIn("if(!loaded)",src)
        self.assertIn('service=="hotspot"',src)
        self.assertIn('service=="pppoe"',src)

if __name__=="__main__": unittest.main()
