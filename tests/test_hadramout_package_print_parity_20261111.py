"""Acceptance contracts for Hadramout-style local package and print snapshots.

The actual SQLite/DPAPI persistence behavior is exercised in
HadramoutBatchPrintSnapshotTests and VoucherPackagePresetPersistenceTests.
"""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/"src/NajmCard.Desktop"
MODELS=ROOT/"src/NajmCard.Core/Models"

def read(path):
    return path.read_text(encoding="utf-8")

class HadramoutPackagePrintParity(unittest.TestCase):
    def test_package_snapshot_contains_every_editor_output_option(self):
        settings=read(SOURCE/"Views/GeneratorView.PackageSettings.cs")
        printer=read(SOURCE/"Views/PrintingView.PrintSelectors.cs")
        store=read(SOURCE/"Services/VoucherStore.PackagePresets.cs")
        self.assertIn("OutputPreferencesProvider?.Invoke()",settings)
        self.assertIn("ApplyOutputPreferences?.Invoke(",settings)
        self.assertIn("PrintOptionsProvider?.Invoke()",settings)
        self.assertIn("ReadCurrentAppearance()",settings)
        self.assertIn("SavePackagePreset(saved)",settings)
        for expected in ("_additionalExport.IsChecked","_csvExport.IsChecked",
                         "_excelExport.IsChecked","_printerSelector?.SelectedItem",
                         "ApplyOutputPreferences("):
            self.assertIn(expected,printer)
        self.assertIn("ON CONFLICT(router_key,service,profile,customer)",store)
        self.assertIn("VoucherOutputPreferences? Output=null",
                      read(MODELS/"VoucherPackagePreset.cs"))

    def test_batch_snapshot_is_durable_and_part_of_card_creation_transaction(self):
        voucher_store=read(SOURCE/"Services/VoucherStore.cs")
        batch_store=read(SOURCE/"Services/VoucherStore.BatchPrintSnapshots.cs")
        creator=read(SOURCE/"Views/GeneratorView.cs")
        self.assertIn("VoucherBatchPrintSnapshot? printSnapshot=null",voucher_store)
        self.assertLess(voucher_store.index("WriteBatchPrintSnapshot(db,tx,printSnapshot)"),
                        voucher_store.index("tx.Commit();",voucher_store.index("public void SaveBatch(")))
        self.assertIn("SaveBatch(newCards,snapshot)",creator)
        self.assertIn("new VoucherBatchPrintSnapshot(",creator)
        self.assertIn("LoadBatchPrintSnapshot(",batch_store)
        self.assertIn("SaveBatchPrintSnapshot(",batch_store)

    def test_history_selection_and_real_print_share_the_same_snapshot(self):
        printer=read(SOURCE/"Views/PrintingView.cs")
        history=read(SOURCE/"Views/PrintingView.BatchSnapshots.cs")
        self.assertIn("RestoreArchivePrintTemplate(",printer)
        self.assertIn("LoadBatchPrintSnapshot(",history)
        self.assertIn("_selectedArchiveTemplate?.Appearance",history)
        self.assertIn("SnapshotForPrint(",history)
        self.assertIn("GetBatchVouchers(latest.Service,latest.BatchId)",history)
        self.assertNotIn("GetPrintableVouchers(",history)
        self.assertIn("store.SaveBatchPrintSnapshot(SnapshotForPrint(",printer)
        self.assertIn("لا تُطبع الحالات CREATED/UNKNOWN/FAILED",history)
        self.assertIn("هذه دفعة قديمة بلا قالب طباعة محفوظ",history)
        self.assertIn("if(_viewingArchive)",history)

if __name__=="__main__":
    unittest.main()
