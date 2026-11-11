"""Regression for cancelled dashboard reload and transactional confirmed PDF output."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src/NajmCard.Desktop/Views"

def read(name):
    return (VIEWS/name).read_text(encoding="utf-8")

class VerifiedDashboardPdfFlow(unittest.TestCase):
    def test_dashboard_marks_initial_read_complete_only_after_router_counts(self):
        src=read("DashboardView.xaml.cs")
        load=src[src.index("Loaded += "):src.index("Unloaded += ")]
        self.assertNotIn("_initialSnapshotLoaded=true",load)
        self.assertIn("if(_refreshing)_refreshAfterUnloaded=true;",load)
        self.assertIn("else _=RefreshAsync();",load)
        refresh=src[src.index("private async Task RefreshAsync()"):
                    src.index("private async Task RefreshActivityAsync")]
        self.assertLess(refresh.index("ReadDashboardLiveCountsAsync("),
                        refresh.index("_initialSnapshotLoaded=true;"))
        self.assertIn("var retry=_refreshAfterUnloaded && IsLoaded && !_initialSnapshotLoaded;",refresh)
        self.assertIn("if(retry)_=RefreshAsync();",refresh)

    def test_lifecycle_cancelled_read_can_resume_after_reentering_dashboard(self):
        src=read("DashboardView.xaml.cs")
        schedule=src[src.index("private async Task ScheduleInitialLifecycleAsync()"):
                     src.index("private async Task RefreshLifecycleAsync()")]
        self.assertIn("_lifecycleScheduleQueued=true;",schedule)
        self.assertIn("await RefreshLifecycleAsync();",schedule)
        self.assertNotIn("_initialLifecycleAttempted=true;",schedule)
        self.assertIn("if(retry)_=ScheduleInitialLifecycleAsync();",schedule)
        lifecycle=src[src.index("private async Task RefreshLifecycleAsync()"):]
        self.assertLess(lifecycle.index("if(!state.IsComplete)"),
                        lifecycle.index("_initialLifecycleAttempted=true;"))

    def test_confirmed_export_never_publishes_before_rechecking_cards(self):
        view=read("PrintingView.cs")
        helper=read("PrintingView.BatchSnapshots.cs")
        self.assertIn("await StageAndPublishVerifiedPdfAsync(",view)
        self.assertIn("stage=>_renderPdfAsync(job.Cards,job.Style,job.Options,stage)",view)
        self.assertIn("()=>SameVerifiedBatch(job.Cards,job.Style,job.Options)",view)
        self.assertIn("preparedCount=cards.Length;designUsed=style;optionsUsed=opts;",view)
        self.assertIn("SameVerifiedBatch(preparedCards,designUsed,optionsUsed)",view)
        self.assertIn("confirmed.SequenceEqual(expected)",helper)
        self.assertLess(helper.index("await render(temp);"),helper.index("if(!verify())"))
        self.assertLess(helper.index("if(!verify())"),helper.index("saveSnapshot();"))
        self.assertLess(helper.index("saveSnapshot();"),helper.index("File.Replace(temp,target,null)"))
        self.assertIn("if(File.Exists(temp))File.Delete(temp);",helper)
        self.assertIn('".najm-verified-"+Guid.NewGuid()',helper)

    def test_native_windows_tests_cover_atomically_verified_pdfs(self):
        test=ROOT/"tests/NajmCard.Desktop.Tests/VerifiedPdfPublicationTests.cs"
        src=test.read_text(encoding="utf-8")
        for requirement in (
            "Invalidate_During_Render_Does_Not_Replace_A_Previous_Pdf",
            "Failed_Snapshot_Save_Does_Not_Replace_An_Existing_Pdf",
            "Validated_Pdf_Is_Atomically_Published_Only_After_Snapshot_Save",
            "Assert.Empty(Directory.GetFiles(folder,",
        ):
            self.assertIn(requirement,src)

if __name__=="__main__":unittest.main()
