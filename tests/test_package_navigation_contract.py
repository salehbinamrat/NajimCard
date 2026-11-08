"""Regression contract: clicking 'create package cards' must select the actual chosen
RouterOS service and profile in the unified generator, not silently choose first."""
from pathlib import Path
import unittest
V=Path(__file__).resolve().parents[1]/"src"/"NajmCard.Desktop"
class PackageNavigationContract(unittest.TestCase):
    def test_selection_routes_to_the_exact_generator_profile(self):
        p=(V/"Views"/"ProfilesView.cs").read_text(encoding="utf-8")
        g=((V/"Views"/"GeneratorView.cs").read_text(encoding="utf-8")
           +(V/"Views"/"GeneratorView.ProfileLoading.cs").read_text(encoding="utf-8"))
        m=(V/"MainWindow.xaml.cs").read_text(encoding="utf-8")
        w=(V/"Views"/"VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        self.assertIn("selectPackage?.Invoke(service,item.Name)",p)
        self.assertIn('new ProfilesView(_gateway, NavigateToVoucherPackage, _store)',m)
        self.assertIn("SelectPackageOnLoad(",w)
        self.assertIn("ApplyPendingPackageSelection(",g)
        self.assertIn('"generator" => new VoucherWorkspaceView(',m)
        self.assertIn("_pendingVoucherService",m)
        self.assertIn("_pendingVoucherProfile",m)

if __name__=="__main__":unittest.main()
