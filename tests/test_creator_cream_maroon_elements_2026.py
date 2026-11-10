"""Acceptance checks for the owner-requested NajmCard creator refactor.
Only the unified generator workspace may change; Hadramout and sidebar are untouched.
"""
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
VIEW=ROOT/"src/NajmCard.Desktop/Views"
CORE=ROOT/"src/NajmCard.Core/Models"

def source(name):
    return (VIEW/name).read_text(encoding="utf-8")

class CreatorCreamBurgundyTests(unittest.TestCase):
    def test_single_package_save_is_upper_only(self):
        generator=source("GeneratorView.cs")
        designer=source("GeneratorView.DesignerPanel.cs")
        self.assertIn('var quickSaveButton=PageUi.Button("حفظ إعدادات الباقة",false)',generator)
        self.assertIn("_store.SavePackagePreset(saved)",generator)
        self.assertNotIn("SaveCurrentPackage()",designer)
        self.assertNotIn("saveAppearanceButton",designer)

    def test_obsolete_template_buttons_are_gone_but_image_import_remains(self):
        designer=source("GeneratorView.DesignerPanel.cs")
        for label in ("تغيير القالب","استيراد قالب","تصدير قالب"):
            self.assertNotIn('PageUi.Button("'+label+'"',designer)
        self.assertIn('PageUi.Button("تغيير الثيم",false)',designer)
        self.assertIn("ChooseTemplateImage()",designer)

    def test_cream_panels_maroon_borders_and_real_color_strip(self):
        preview=source("GeneratorView.CardDesign.cs")
        designer=source("GeneratorView.DesignerPanel.cs")
        workspace=source("VoucherWorkspaceView.cs")
        self.assertIn("#FFF4DF",preview)
        self.assertIn("#75203E",preview)
        self.assertIn("#FFFAF0",workspace)
        self.assertIn("PaletteButton(",designer)
        self.assertIn("popup.IsOpen=!popup.IsOpen",designer)
        self.assertIn('ShapeOption("العرض (مم)"',designer)
        self.assertIn('ShapeOption("الطول (مم)"',designer)
        self.assertIn('ShapeOption("حجم QR"',designer)
        self.assertIn('ShapeOption("سمك الحدود"',designer)

    def test_all_reference_elements_are_visible_and_positioned_for_pdf(self):
        controls=source("GeneratorView.DesignerPanel.cs")
        preview=source("GeneratorView.CardDesign.cs")
        printer=source("PrintingView.cs")
        appearance=(CORE/"VoucherCardAppearance.cs").read_text(encoding="utf-8")
        for name in ("Capacity","Phone","Qr","Time","Date","Title","Sequence","Route"):
            self.assertIn("_designShow"+name,controls)
            self.assertIn("Show"+name+" =",appearance)
        for name in ("CapacityPosition","PhonePosition","QrPosition","TimePosition",
                     "DatePosition","SequencePosition","RoutePosition"):
            self.assertIn(name,appearance)
            self.assertIn(name,preview)
            self.assertIn(name,printer)
        self.assertIn("CardQrRenderer.Render(username)",preview)
        self.assertIn("CardQrRenderer.Render(card.Username)",printer)
        qr=source("CardQrRenderer.cs")
        self.assertNotIn("card.Password",qr)

    def test_credential_layers_drag_independently_and_remain_transparent(self):
        preview=source("GeneratorView.CardDesign.cs")
        self.assertIn("EnableCardEditing(elements,usernamePlate",preview)
        self.assertIn("EnableCardEditing(elements,passwordPlate",preview)
        self.assertIn("CardCredentialPlate.Build(_previewPassword",preview)
        self.assertIn("Background=Brushes.Transparent",preview)
        self.assertIn("CredentialsPosition=position",preview)
        self.assertIn("PasswordPosition=position",preview)

if __name__=="__main__":
    unittest.main()
