"""Parity guards for Hadramout package snapshots and physical print preflight."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src/NajmCard.Desktop/Views"
STORE=ROOT/"src/NajmCard.Desktop/Services/VoucherStore.PackagePresets.cs"

class HadramoutPackageAndPrintParity(unittest.TestCase):
    def test_package_snapshot_is_durable_and_scoped(self):
        s=STORE.read_text(encoding="utf-8")
        gen=(VIEWS/"GeneratorView.cs").read_text(encoding="utf-8")
        self.assertIn("ON CONFLICT(router_key,service,profile,customer)",s)
        self.assertIn("using var tx=db.BeginTransaction()",s)
        self.assertIn("tx.Commit()",s)
        self.assertIn("JsonSerializer.Serialize(preset)",s)
        self.assertIn("LoadPackagePreset(_routerKey,SelectedService,profile,owner)",gen)
        self.assertIn("PrintOptionsProvider?.Invoke()",gen)
        self.assertIn("PackagePrintSettingsRestored?.Invoke(v?.Print",gen)
        self.assertIn("ReadCurrentAppearance()",gen)

    def test_owner_change_invalidates_pending_batch_before_restore(self):
        s=(VIEWS/"GeneratorView.cs").read_text(encoding="utf-8")
        owner=s[s.index("_customer.SelectionChanged +="):]
        self.assertLess(owner.index("_batch=[]"),owner.index("RestorePackagePreset();"))
        self.assertLess(owner.index("BatchChanged?.Invoke()"),owner.index("RestorePackagePreset();"))

    def test_physical_print_checks_snapshot_twice(self):
        p=(VIEWS/"PrintingView.cs").read_text(encoding="utf-8")
        helper=(VIEWS/"PrintingView.Preflight.cs").read_text(encoding="utf-8")
        self.assertIn("preparedCards=cards.ToArray()",p)
        self.assertIn("preparedCards=[]",p)
        self.assertGreaterEqual(p.count("if(!VerifyPreparedBatch())"),2)
        self.assertIn("confirmed.SequenceEqual(prepared)",helper)
        self.assertIn('v.DeliveryStatus=="SENT"&&v.SentToRouter',helper)
        self.assertIn("Equals(previewStyle,currentStyle.Validate())",helper)
        self.assertLess(p.index("if(!VerifyPreparedBatch())",p.index("void Print()")),
                        p.index("dlg.PrintDocument(prepared!",p.index("void Print()")))

    def test_pdf_previews_remain_temporary_and_distinct_from_printed_cards(self):
        p=(VIEWS/"PrintingView.Preflight.cs").read_text(encoding="utf-8")
        d=(VIEWS/"PrintingView.DraftDelivery.cs").read_text(encoding="utf-8")
        self.assertIn('Path.Combine(directory,"Najm_"+Guid.NewGuid()',p)
        self.assertIn('OverwritePrompt=true',p)
        self.assertIn('غير مفعلة في الراوتر',d)
        self.assertIn('worker.SetApartmentState(ApartmentState.STA)',d)

if __name__=="__main__":
    unittest.main()
