"""Creation/delivery progress popup follows real operations, not a timer or fake percent."""

from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
V=ROOT/"src"/"NajmCard.Desktop"/"Views"
SERVICES=ROOT/"src"/"NajmCard.Desktop"/"Services"

def read(path):
    return path.read_text(encoding="utf-8")

class VoucherProgressPopupContract(unittest.TestCase):
    def test_compact_non_modal_progress_has_arabic_stages(self):
        popup=read(V/"VoucherOperationProgressWindow.cs")
        self.assertIn("class VoucherOperationProgressWindow : Window",popup)
        self.assertIn("Width=395",popup)
        self.assertIn("Height=176",popup)
        self.assertIn("ShowInTaskbar=false",popup)
        self.assertIn("WindowStartupLocation.CenterOwner",popup)
        self.assertIn("IsIndeterminate=true",popup)
        self.assertIn("public void UpdateStage(string stage)",popup)
        self.assertIn("FlowDirection=FlowDirection.RightToLeft",popup)

    def test_both_issuance_actions_show_and_close_progress_in_finally(self):
        workspace=read(V/"VoucherWorkspaceView.cs")
        self.assertIn('WithCardProgressAsync("إنشاء وإرسال وحفظ الكروت"',workspace)
        self.assertIn('WithCardProgressAsync("إنشاء وإرسال الكروت"',workspace)
        self.assertIn("creator.OperationProgressChanged+=ShowStage;",workspace)
        self.assertIn("creator.OperationProgressChanged-=ShowStage;",workspace)
        self.assertIn("popup.Show();",workspace)
        self.assertIn("popup.Close();",workspace)
        self.assertIn('report("حفظ ملف PDF الأولي',workspace)
        self.assertIn('report("حفظ ملف PDF النهائي',workspace)
        # The PDF progress begins only after a real folder was selected.
        self.assertLess(workspace.index("if(picker.ShowDialog()!=true)return;"),
                        workspace.index('WithCardProgressAsync("إنشاء وإرسال وحفظ الكروت"'))

    def test_progress_stages_originate_from_real_issue_and_router_commands(self):
        creator=read(V/"GeneratorView.cs")
        mode=read(V/"GeneratorView.CredentialMode.cs")
        dispatch=read(SERVICES/"VoucherBatchDispatcher.cs")
        self.assertIn("public event Action<string>? OperationProgressChanged;",mode)
        self.assertIn('ReportOperationProgress("توليد أرقام الكروت...")',creator)
        self.assertIn('saveStage.Report("حفظ الكروت في قاعدة البيانات المحلية...")',creator)
        self.assertIn("_store.SaveBatch(newCards,snapshot);",creator)
        self.assertLess(creator.index('saveStage.Report("حفظ الكروت'),
                        creator.index('_store.SaveBatch(newCards,snapshot);'))
        self.assertIn("progress:new Progress<string>(ReportOperationProgress)",creator)
        self.assertIn('progress?.Report("التحقق من الأرقام',dispatch)
        self.assertIn('progress?.Report($"إرسال الكرت',dispatch)
        self.assertIn('using var sendSession = store.OpenSendSession();',dispatch)
        self.assertIn('sendSession.FinishSend(voucher, state, error);',dispatch)
        self.assertIn('progress?.Report($"تسجيل نتيجة الكرت',dispatch)
        self.assertLess(dispatch.index("sendSession.FinishSend(voucher, state, error);"),
                        dispatch.index('progress?.Report($"تسجيل نتيجة الكرت'))
        self.assertIn("progressStride",dispatch)
        self.assertIn("phaseSeconds",read(V/"VoucherWorkspaceView.cs"))
        self.assertNotIn("Task.Delay(",popup if (popup := read(V/"VoucherOperationProgressWindow.cs")) else "")

if __name__=="__main__":
    unittest.main()
