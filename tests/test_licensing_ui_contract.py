"""Contract tests for the dual trial/paid licensing paths. They run with Windows CI."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
APP=ROOT/"src"/"NajmCard.Desktop"
class LicensingUiContract(unittest.TestCase):
    def test_trial_has_no_network_operations_and_is_app_specific(self):
        trial=(APP/"Services/Licensing/LocalTrialService.cs").read_text(encoding="utf-8")
        for forbidden in ("HttpClient","https://","/v1/claim","HttpRequestMessage","WebRequest"):
            self.assertNotIn(forbidden,trial)
        self.assertIn("BeginAfterSuccessfulRouterLogin",trial)
        self.assertIn("TimeSpan.FromDays(20)",trial)
        self.assertIn("StartedAtUtc",trial)
    def test_paid_uses_exact_existing_hadramout_backend_and_fingerprint(self):
        code=(APP/"Services/Licensing/PaidLicenseClient.cs").read_text(encoding="utf-8")
        fingerprint=(APP/"Services/Licensing/WindowsLicenseFingerprint.cs").read_text(encoding="utf-8")
        self.assertIn("zlirqppwemfmzxkholcr.supabase.co",code)
        self.assertIn("hadramout-license",code)
        self.assertIn("NIDI-MIKROTIK-CARDS",code)
        self.assertIn("HADRAMOUT-GITHUB-LICENSE-V1|",fingerprint)
        self.assertIn("RegistryView.Registry64",fingerprint)
        self.assertNotIn("service_role",code.lower())
    def test_premium_trial_ui_and_gating_after_successful_mikrotik_connection(self):
        xaml=(APP/"MainWindow.xaml").read_text(encoding="utf-8")
        code=(APP/"MainWindow.xaml.cs").read_text(encoding="utf-8")
        for value in ("TrialButton","LicenseCodeInput","ActivateLicenseButton",
                      "RenewLicenseButton","LicenseNotice","20 يوما"):
            self.assertIn(value,xaml)
        for value in ("OnSuccessfulRouterLogin()","ActivateAsync(",
                      "VerifyAsync(","LicenseGate",
                      "RouterOsClient.ConnectAsync("):
            self.assertIn(value,code)
        self.assertIn("await session.DisposeAsync()",code)
        trial=(APP/"Services/Licensing/LocalTrialService.cs").read_text(encoding="utf-8")
        self.assertIn("trial.BeginAfterSuccessfulRouterLogin()",trial)
if __name__=="__main__":unittest.main()
