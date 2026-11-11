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


    def test_print_preflight_detects_stale_batches_on_both_confirmations(self):
        printer=read(SOURCE/"Views/PrintingView.cs")
        snapshots=read(SOURCE/"Views/PrintingView.BatchSnapshots.cs")
        self.assertIn("preparedCards=cards.ToArray()",printer)
        self.assertIn("preparedCards=[]",printer)
        self.assertEqual(2,printer.count("if(!VerifyPreparedBatch())"))
        self.assertIn('store.GetBatchVouchers(archive.Service,archive.BatchId)',snapshots)
        self.assertIn('confirmed.SequenceEqual(expected)',snapshots)
        self.assertIn('card.DeliveryStatus=="SENT"',snapshots)
        self.assertIn('card.SentToRouter',snapshots)
        self.assertIn('Equals(originalStyle,currentStyle.Validate())',snapshots)
        self.assertLess(printer.index('if(!VerifyPreparedBatch())'),
                        printer.index('dlg.PrintDocument(prepared!'))

    def test_printer_absence_must_not_silently_redirect_to_default(self):
        selector=read(SOURCE/"Views/PrintingView.PrintSelectors.cs")
        printer=read(SOURCE/"Views/PrintingView.cs")
        self.assertIn('selectedPrinter.Items.Add(selected)',selector)
        self.assertIn('غير مثبتة على Windows الحالي',selector)
        self.assertIn('chosenServer.GetPrintQueue(printerName)',printer)
        self.assertIn('submittedToSpooler=true',printer)
        self.assertIn('راجع قائمة انتظار الطابعة والورق',printer)

    def test_customer_changes_invalidate_pending_batch_and_keep_packages_scoped(self):
        creator=read(SOURCE/"Views/GeneratorView.cs")
        settings=read(SOURCE/"Views/GeneratorView.PackageSettings.cs")
        change=creator[creator.index('_customer.SelectionChanged +='):]
        self.assertLess(change.index('_batch=[];'),
                        change.index('RestorePackagePreset();'))
        self.assertIn('if(_restoringPreset || _applyingProfileCatalog)return;',change)
        self.assertIn('LoadPackagePreset(_routerKey,SelectedService,profile,owner)',settings)
        self.assertIn('ApplyOutputPreferences?.Invoke(',settings)

    def test_bulk_sqlite_insert_is_prepared_and_durable(self):
        store=read(SOURCE/"Services/VoucherStore.cs")
        self.assertIn('cmd.Prepare();',store)
        self.assertIn('using var tx=db.BeginTransaction()',store)
        self.assertIn('PRAGMA journal_mode=WAL',store)
        self.assertIn('PRAGMA synchronous=FULL',store)
        self.assertIn('WriteBatchPrintSnapshot(db,tx,printSnapshot)',store)

if __name__=="__main__":
    unittest.main()
