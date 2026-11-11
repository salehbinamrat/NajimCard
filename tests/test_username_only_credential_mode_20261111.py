"""Regression contracts for explicit username-only vs password voucher issuance."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
DESKTOP = ROOT / "src" / "NajmCard.Desktop" / "Views"
CORE = ROOT / "src" / "NajmCard.Core" / "Services"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class CredentialModeIsolation(unittest.TestCase):
    def test_read_options_never_reuse_matching_flag_in_username_only(self):
        live = source(DESKTOP / "GeneratorView.LivePreview.cs")
        self.assertIn(
            "bool same=!usernameOnly && _credentialsSame.IsChecked==true;", live
        )
        self.assertIn("UsernameOnly:usernameOnly", live)

    def test_saved_package_does_not_conflate_the_two_modes(self):
        saved = source(DESKTOP / "GeneratorView.PackageSettings.cs")
        self.assertIn(
            "bool same=!usernameOnly && _credentialsSame.IsChecked==true;", saved
        )
        self.assertIn("usernameOnly,", saved)

    def test_switching_credential_mode_invalidates_earlier_batch(self):
        creator = source(DESKTOP / "GeneratorView.cs") + source(
            DESKTOP / "GeneratorView.CredentialMode.cs"
        )
        section = creator.split(
            "private void InvalidatePendingBatchForCredentialChange()", 1
        )[1].split("private void SetCredentialControlsBusy(", 1)[0]
        self.assertIn("_batch = [];", section)
        self.assertIn("_sendButton.IsEnabled = false;", section)
        self.assertIn("BatchChanged?.Invoke();", section)
        self.assertIn(
            "_credentialMode.SelectionChanged+=(_,_)=>CredentialChoiceChanged();",
            creator,
        )
        self.assertIn(
            "_credentialsSame.Checked+=(_,_)=>CredentialChoiceChanged();",
            creator,
        )
        self.assertIn(
            "_credentialsDifferent.Checked+=(_,_)=>CredentialChoiceChanged();",
            creator,
        )
        self.assertIn("DescribeSavedCredentialMode(_batch)", creator)

    def test_hotspot_omits_blank_password_like_hadramout(self):
        gateway = source(CORE / "RouterGateway.cs")
        send = gateway.split("public async Task AddHotspotUserAsync(", 1)[1].split(
            "public async Task<BatchDeliveryResult> UploadBatchAsync(", 1
        )[0]
        self.assertIn("if (voucher.Password.Length > 0)", send)
        self.assertIn('args["password"] = voucher.Password;', send)
        self.assertNotIn('["password"] = voucher.Password,', send)


    def test_editing_other_generation_fields_invalidates_existing_batch(self):
        creator = source(DESKTOP / "GeneratorView.cs")
        mode = source(DESKTOP / "GeneratorView.CredentialMode.cs")
        package = source(DESKTOP / "GeneratorView.PackageSettings.cs")
        self.assertIn("SubscribeGenerationInputChanges();", creator)
        self.assertIn("private void SubscribeGenerationInputChanges()", mode)
        for field in (
            "_count", "_pagesEstimate", "_prefix", "_length",
            "_usernameCharacters", "_passwordLength", "_passwordCharacters",
            "_usernameAlphabetType", "_passwordAlphabetType",
        ):
            self.assertIn(field, mode)
        self.assertIn(
            "input.TextChanged += (_, _) => InvalidatePendingBatchForCredentialChange();",
            mode,
        )
        self.assertIn("selector.SelectionChanged += (_, _) =>", mode)
        self.assertIn("_lengthWithPrefix.Checked +=", mode)
        self.assertIn("_lengthWithPrefix.Unchecked +=", mode)
        self.assertIn("input.IsEnabled = !busy;", mode)
        self.assertIn("selector.IsEnabled = !busy;", mode)
        self.assertIn(
            "_restoringPreset=false;InvalidatePendingBatchForCredentialChange();",
            package,
        )


if __name__ == "__main__":
    unittest.main()
