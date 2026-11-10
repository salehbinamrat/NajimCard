"""Dense Hadramout creator geometry and non-issuing live preview contracts.

These static checks intentionally complement, not replace, WPF renderer and
real voucher-generator tests on the Windows runners. Navigation is out of scope.
"""
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src"/"NajmCard.Desktop"/"Views"
CORE=ROOT/"src"/"NajmCard.Core"/"Services"


class CreatorLivePreviewDensity(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        def view(name):
            return (VIEWS/name).read_text(encoding="utf-8")
        cls.layout=view("GeneratorView.ReferenceLayout.cs")
        cls.creator=view("GeneratorView.cs")
        cls.live=view("GeneratorView.LivePreview.cs")
        cls.card=view("GeneratorView.CardDesign.cs")
        cls.designer=view("GeneratorView.DesignerPanel.cs")
        cls.workspace=view("VoucherWorkspaceView.cs")
        cls.printer=view("PrintingView.cs")
        cls.generator=(CORE/"VoucherGenerator.cs").read_text(encoding="utf-8")

    def test_original_hadramout_column_proportions(self):
        self.assertIn("new GridLength(42,GridUnitType.Star)",self.layout)
        self.assertIn("new GridLength(58,GridUnitType.Star)",self.layout)
        self.assertNotIn("new GridLength(48,GridUnitType.Star)",self.layout)
        self.assertNotIn("new GridLength(52,GridUnitType.Star)",self.layout)
        self.assertIn("Grid.SetColumn(leftColumn,0)",self.layout)
        self.assertIn("Grid.SetColumn(rightColumn,1)",self.layout)

    def test_content_fills_width_without_nested_gutters(self):
        self.assertIn("surface.Margin=new Thickness(1,1,1,2)",self.layout)
        self.assertIn("surface.Padding=new Thickness(6)",self.card)
        self.assertIn("surface.Margin=new Thickness(0)",self.card)
        self.assertIn("printPanel.Margin=new Thickness(0)",self.printer)
        self.assertNotIn("fields.Width=1140",self.workspace)
        self.assertIn("StretchDirection=StretchDirection.Both",self.creator)
        self.assertIn("HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled",self.workspace)

    def test_card_preview_is_live_and_does_not_use_sample_literal(self):
        self.assertNotIn("1111111111",self.creator)
        self.assertIn("SubscribeLiveCardPreview();",self.creator)
        self.assertIn("finally {_restoringPreset=false;UpdateLiveCardPreview();}",self.creator)
        self.assertIn("VoucherGenerator.PreviewSample(options)",self.live)
        self.assertIn("ReadGenerationOptions(forPreview:false)",self.creator)
        for field in ("_count","_prefix","_length","_passwordLength",
                      "_usernameCharacters","_passwordCharacters"):
            self.assertIn(field,self.live)
        self.assertIn("_credentialMode.SelectedIndex==0",self.live)
        self.assertIn("_lengthWithPrefix.Checked+=",self.live)
        self.assertIn("_lengthWithPrefix.Unchecked+=",self.live)
        self.assertIn("PasswordCharacters:same?null:_passwordCharacters.Text.Trim()",self.live)

    def test_preview_reuses_generator_rules_without_io(self):
        self.assertIn("private static PreparedGeneration Prepare(",self.generator)
        self.assertIn("public static (string Username, string Password) PreviewSample(",self.generator)
        self.assertIn("var p=Prepare(options);",self.generator)
        self.assertNotIn("_store.",self.live)
        self.assertNotIn("_gateway.",self.live)
        self.assertNotIn("SaveBatch(",self.live)
        self.assertNotIn("UploadAsync(",self.live)
        self.assertNotIn("new Voucher(",self.live)

    def test_artwork_contrast_and_dropdown_selection_are_readable(self):
        self.assertIn('Tag="CardArtwork"',self.card)
        self.assertIn('Equals(element.Tag,"CardArtwork")',self.card)
        theme=(VIEWS.parent/"Themes"/"BurgundyGold.xaml").read_text(encoding="utf-8")
        self.assertIn('FindResource("DarkComboBox")',self.workspace)
        self.assertIn('SelectionBoxItemTemplate',theme)
        self.assertIn('TargetType="ComboBoxItem"',theme)
        self.assertNotIn('#FFF5E7',self.workspace)
        self.assertIn("printPanel.VerticalAlignment=VerticalAlignment.Top",self.printer)

    def test_footer_docked_and_sidebar_not_touched(self):
        self.assertIn("DockPanel.SetDock(footer,Dock.Bottom)",self.workspace)
        self.assertIn("buttonsGrid=new UniformGrid{Columns=7",self.workspace)
        self.assertIn("Content=root;",self.workspace)
        self.assertNotIn("new MainWindow(",self.live+self.layout+self.card+self.workspace)
        self.assertIn("VerticalScrollBarVisibility=ScrollBarVisibility.Auto",self.workspace)


if __name__=="__main__":
    unittest.main()
