"""Second-round acceptance tests for the owner's exact 1648x928 card-page reference.

These contracts deliberately fail against prior merged-in PR #5 behavior. They
complement Windows WPF compilation, live data tests, and visual acceptance.
"""
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]/"src"/"NajmCard.Desktop"
VIEW=ROOT/"Views"
class ReferenceImageFinalFit(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.main=(ROOT/"MainWindow.xaml.cs").read_text(encoding="utf-8")
        cls.layout=(VIEW/"GeneratorView.ReferenceLayout.cs").read_text(encoding="utf-8")
        cls.design=(VIEW/"GeneratorView.DesignerPanel.cs").read_text(encoding="utf-8")
        cls.creator=(VIEW/"GeneratorView.cs").read_text(encoding="utf-8")
        cls.card=(VIEW/"GeneratorView.CardDesign.cs").read_text(encoding="utf-8")
        cls.printing=(VIEW/"PrintingView.cs").read_text(encoding="utf-8")
        cls.workspace=(VIEW/"VoucherWorkspaceView.cs").read_text(encoding="utf-8")
        cls.pageui=(VIEW/"PageUi.cs").read_text(encoding="utf-8")
        cls.theme=(ROOT/"Themes"/"BurgundyGold.xaml").read_text(encoding="utf-8")
    def test_creator_uses_full_available_page_instead_of_hidden_title_and_side_gutters(self):
        self.assertIn('isImmersive=page.Key is "dashboard" or "generator"',self.main)
        self.assertIn("WorkspaceHeaderRow.Height=isImmersive",self.main)
        self.assertIn("new Thickness(12,7,12,7)",self.main)
        self.assertNotIn("fields.Width=1140",self.workspace)
        self.assertIn("VerticalScrollBarVisibility=ScrollBarVisibility.Hidden",self.workspace)
        self.assertIn("HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled",self.workspace)
    def test_short_windows_screen_keeps_footer_visible_and_forms_accessible(self):
        # On 1366x768 laptops, full-screen reference proportions cannot fit
        # without either adaptive scaling or a content-only vertical scroll.
        # The six-button footer must never disappear off the physical display.
        win=(ROOT/"MainWindow.xaml").read_text(encoding="utf-8")
        self.assertIn("SystemParameters.WorkArea",self.main)
        self.assertIn("MinHeight",win)
        self.assertIn("VerticalScrollBarVisibility=ScrollBarVisibility.Hidden",self.workspace)
        self.assertIn("HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled",self.workspace)
        self.assertIn("DockPanel.SetDock(footer,Dock.Bottom)",self.workspace)

    def test_routing_and_user_inputs_are_side_by_side_above_print_and_design(self):
        self.assertIn("Grid.SetColumn(routeSurface,1)",self.layout)
        self.assertIn("full.Children.Add(formatSurface)",self.layout)
        self.assertIn("Width=new GridLength(41,GridUnitType.Star)",self.layout)
        self.assertIn("Width=new GridLength(59,GridUnitType.Star)",self.layout)
        self.assertIn("printColumn.Children.Add(basicsSurface)",self.layout)
        self.assertIn("printColumn.Children.Add(_embeddedPrintHost)",self.layout)
        self.assertIn("Grid.SetColumn(designSurface,1)",self.layout)
        self.assertNotIn("leftColumn.RowDefinitions",self.layout)
    def test_photo_card_preview_is_responsive_without_cutting_actual_artwork(self):
        self.assertIn("new Viewbox",self.creator)
        self.assertIn("Stretch=Stretch.Uniform",self.creator)
        self.assertIn("Child=_appearancePreviewBorder",self.creator)
        self.assertIn("ClipToBounds=true",self.card)
        self.assertIn("TemplateImagePath",self.printing)
        self.assertIn("ChooseTemplateImage()",self.design)
    def test_basic_generation_has_five_working_columns_and_page_count_in_sync(self):
        self.assertIn("Columns = 5",self.creator)
        self.assertIn('("عدد الصفحات", _pagesEstimate)',self.creator)
        self.assertIn('("طول كلمة المرور", _passwordLength)',self.creator)
        page_sync=(VIEW/"GeneratorView.PageCount.cs").read_text(encoding="utf-8")
        self.assertIn("public void RefreshPageCount()",page_sync)
        self.assertIn("CardBatchQuantityPlanner.CardsForSheets",page_sync)
        self.assertIn("CardBatchQuantityPlanner.SheetsForCards",page_sync)
        self.assertIn("_pagesEstimate.IsReadOnly=false",self.creator)
        self.assertIn("LayoutSettingsChanged",self.printing)
        self.assertIn("printer.LayoutSettingsChanged+=creator.RefreshPageCount",self.workspace)
        self.assertIn("creator.RefreshPageCount()",self.workspace)
    def test_advanced_editor_and_preset_are_real_not_fake_buttons(self):
        self.assertIn("WindowStartupLocation.CenterOwner",self.design)
        self.assertIn("popup.ShowDialog()",self.design)
        self.assertIn("ApplyCardAppearance(ReadCurrentAppearance())",self.design)
        self.assertNotIn("SaveCurrentPackage()",self.design)
        self.assertIn("WirePackageSaveNotification(quickSaveButton)",self.creator)
        self.assertIn("var saved=SaveCurrentPackage()",
            (VIEW/"GeneratorView.SaveToast.cs").read_text(encoding="utf-8"))
        self.assertIn("TemplateImagePath",self.card)
    def test_both_windows_release_targets_are_tested_before_four_packages(self):
        ci=(ROOT.parents[1]/".github/workflows/windows-ci.yml").read_text(encoding="utf-8")
        self.assertIn("arch: [x86, x64]",ci)
        self.assertIn("needs: [windows-core, windows-release]",ci)
        self.assertIn("Tests before packaging",ci)
        self.assertIn("Build verified Portable and Installer",ci)
        self.assertIn("SHA256SUMS-",ci)

    def test_shared_pages_have_compact_geometry_and_readable_alternating_tables(self):
        self.assertIn("Padding=new Thickness(12)",self.pageui)
        self.assertIn("Margin=new Thickness(0,0,0,8)",self.pageui)
        self.assertIn('Property="Foreground" Value="{StaticResource TextBrush}"',self.theme)
        self.assertIn('Property="RowBackground" Value="#FFF8EC"',self.theme)
        self.assertIn('Property="Foreground" Value="{StaticResource CreamTextBrush}"',self.theme)
        self.assertIn('TargetType="DataGridColumnHeader"',self.theme)
if __name__=="__main__":unittest.main()
