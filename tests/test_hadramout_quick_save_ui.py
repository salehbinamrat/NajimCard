"""Regression: Hadramout-style package save and preview on NajmCard WPF UI."""
from pathlib import Path
import unittest

VIEW=Path(__file__).resolve().parents[1]/"src"/"NajmCard.Desktop"/"Views"

class HadramoutQuickSaveUi(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.g=(VIEW/"GeneratorView.cs").read_text(encoding="utf-8")+(VIEW/"GeneratorView.PackageSettings.cs").read_text(encoding="utf-8")
        cls.w=(VIEW/"VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        cls.toast=(VIEW/"GeneratorView.SaveToast.cs").read_text(encoding="utf-8")

    def test_direct_package_save_uses_existing_sqlite_preset(self):
        s=self.g
        self.assertIn('PageUi.Button("حفظ إعدادات الباقة",false)',s)
        self.assertIn('WirePackageSaveNotification(quickSaveButton)',s)
        self.assertIn("saveButton.Click+=",self.toast)
        self.assertIn('var saved=SaveCurrentPackage()',self.toast)
        self.assertIn('_store.SavePackagePreset(saved)',s)
        self.assertIn('PrintOptionsProvider?.Invoke()',s)
        self.assertIn('ReadCurrentAppearance()',s)
        self.assertNotIn("packageSaveFeedback",s)

    def test_username_only_is_default_even_for_new_unsaved_package(self):
        s=self.g
        self.assertIn('ItemsSource=new[]{"اسم مستخدم فقط","اسم مستخدم وكلمة مرور"},\n        SelectedIndex=0',s)
        self.assertIn('v?.UsernameOnly==false?1:0',s)
        self.assertIn("passwordModeRow.IsEnabled=!userOnly",s)
        self.assertIn("Grid.SetColumn(passwordModeRow,1)",s)

    def test_save_feedback_appears_only_in_the_toast(self):
        s=self.g
        self.assertNotIn("packageSaveFeedback",s)
        self.assertIn("message.Text=_status.Text",self.toast)
        self.assertIn('_status.Text="";',self.toast)
        self.assertIn('if(_sending||_preparing||_restoringPreset)',s)

    def test_pre_creation_preview_always_uses_local_draft(self):
        self.assertIn("Execute(PreviewDraftPdf)",self.w)
        self.assertIn("await creator.BuildDraftPreviewAsync()",self.w)

    def test_draft_pdf_uses_current_options_without_changing_the_issued_batch(self):
        start=self.g.index("internal Task<Voucher[]> BuildDraftPreviewAsync()")
        end=self.g.index("public async Task<bool> CreateAndSendAsync(",start)
        draft=self.g[start:end]
        self.assertIn("ReadGenerationOptions(forPreview:true)",draft)
        self.assertIn("VoucherGenerator.Generate(options)",draft)
        self.assertIn("ShowPassword=ShowPreviewPassword",draft)
        for side_effect in ("_store", "_gateway", "_batch", "UploadAsync"):
            self.assertNotIn(side_effect,draft)
        self.assertIn("printer.GetOptions()",self.w)
        self.assertIn("printer.CreateDraftPreviewPdfAsync(cards,appearance,options)",self.w)

    def test_confirmed_pdf_uses_worker_with_snapshot_and_sent_filter(self):
        printer=(VIEW/"PrintingView.cs").read_text(encoding="utf-8")+(VIEW/"PrintingView.BatchSnapshots.cs").read_text(encoding="utf-8")
        self.assertIn('card.SentToRouter &&',printer)
        self.assertIn('card.DeliveryStatus=="SENT"',printer)
        self.assertIn("await _renderPdfAsync(job.Cards,job.Style,job.Options,path)",printer)
        draft_worker=(VIEW/"PrintingView.DraftDelivery.cs").read_text(encoding="utf-8")
        self.assertIn("worker.SetApartmentState(ApartmentState.STA)",draft_worker)
        self.assertIn("RenderDocumentPdfOnStaAsync(",printer)
        self.assertIn("await printer.SavePdfAsync()",self.w)
        self.assertIn("status.Text=printer.LastOperationMessage",self.w)

if __name__=="__main__":
    unittest.main()
