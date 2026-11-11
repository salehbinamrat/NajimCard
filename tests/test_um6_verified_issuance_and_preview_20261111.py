"""RouterOS v6 issuance / printing parity with the Hadramout reference.

The native Windows unit suite also exercises exact-ID readback with mocked
RouterOS responses. These checks guard the UI: PDF must never imply that
an unissued sample is a working paid voucher.
"""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
CORE=ROOT/"src/NajmCard.Core/Services"
VIEWS=ROOT/"src/NajmCard.Desktop/Views"
SERVICES=ROOT/"src/NajmCard.Desktop/Services"

class UserManagerDeliveryParity(unittest.TestCase):
    def test_creation_is_two_phase_and_readback_confirms_actual_profile(self):
        src=(CORE/"RouterGateway.cs").read_text(encoding="utf-8")
        self.assertIn('"/tool/user-manager/user/add"',src)
        self.assertIn('"/tool/user-manager/user/create-and-activate-profile"',src)
        self.assertIn('["numbers"] = id',src)
        self.assertIn('const string verifyPath = "/tool/user-manager/user/print"',src)
        self.assertIn('lookup.QueryByIdAsync(verifyPath,id,projection,ct)',src)
        self.assertIn('Get(actual,"username"),voucher.Username',src)
        self.assertIn('Get(actual,"actual-profile"),voucher.Profile',src)
        self.assertIn('return new UserManagerV6DeliveryResult(true,false,id,null)',src)
        self.assertIn('RouterOS readback failures are NOT activation traps',src)
        self.assertIn('لا تُعد إصدار الكرت أو طباعته قبل التحقق',src)

    def test_durable_write_and_safe_print_order(self):
        dispatch=(SERVICES/"VoucherBatchDispatcher.cs").read_text(encoding="utf-8")
        printer=(VIEWS/"PrintingView.cs").read_text(encoding="utf-8")+(VIEWS/"PrintingView.BatchSnapshots.cs").read_text(encoding="utf-8")
        self.assertLess(dispatch.index("store.BeginSend(voucher);"),
                        dispatch.index("gateway.CreateAndActivateUserManagerV6Async("))
        self.assertIn('state = result.Success ? "SENT" :',dispatch)
        self.assertIn('result.UncertainOutcome ? "UNKNOWN" : "FAILED"',dispatch)
        self.assertIn('store.FinishSend(voucher, state, error)',dispatch)
        self.assertIn('card.SentToRouter &&',printer)
        self.assertIn('card.DeliveryStatus=="SENT"',printer)
        self.assertIn('لا تُطبع الحالات CREATED/UNKNOWN/FAILED',printer)

    def test_preview_and_presend_pdf_are_visibly_invalid_and_never_promoted(self):
        ui=(VIEWS/"VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        printer=(VIEWS/"PrintingView.cs").read_text(encoding="utf-8")+(VIEWS/"PrintingView.BatchSnapshots.cs").read_text(encoding="utf-8")
        self.assertIn('printer.SaveDraftArchivePdfAsync(cards,appearance,options,savedBeforeSend)',ui)
        self.assertIn('await printer.SaveBatchArchivePdfAsync(confirmed,appearance,options,path)',ui)
        self.assertNotIn('File.Move(savedBeforeSend!',ui)
        self.assertIn('if(previewOnly)',printer)
        draft=(VIEWS/"PrintingView.DraftDelivery.cs").read_text(encoding="utf-8")
        self.assertIn('معاينة تجريبية',draft)
        self.assertIn('غير مفعلة في الراوتر',draft)
        self.assertIn('if(previewOnly)',printer)
        self.assertIn('MakeDocument(cards,options,appearance,true)',printer)
        self.assertIn('AddDraftWatermark(page,',printer)
        self.assertIn('_renderDraftPdfAsync(cards,appearance,options,path)',printer)
        self.assertIn('if(!creator.HasBatch && !printer.HasSavedBatchSelection)',ui)
        self.assertNotIn('else await PreviewDraftPdf();',ui)

    def test_six_buttons_keep_the_operator_order(self):
        ui=(VIEWS/"VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        names=["إنشاء وإرسال وحفظ","إنشاء وإرسال فقط",
               "معاينة قبل الإنشاء","ملف PDF","معاينة PDF","طباعة الدفعة"]
        indexes=[ui.index('MakeButton("'+name+'"') for name in names]
        self.assertEqual(indexes,sorted(indexes))

if __name__=="__main__":
    unittest.main()
