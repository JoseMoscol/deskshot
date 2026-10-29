"""Verificación independiente de los PDFs sintéticos de DeskShot; no depende del escritor C#."""
from pathlib import Path
import sys
from PIL import Image
from pypdf import PdfReader

root = Path(sys.argv[1]).resolve()
reader = PdfReader(root / "capture-tools.pdf", strict=True)
sources = []
for path in sorted(root.glob("pdf-source-*.png")):
    with Image.open(path) as source:
        rgba = source.convert("RGBA")
        canvas = Image.new("RGBA", rgba.size, "white")
        canvas.alpha_composite(rgba)
        sources.append(canvas.convert("RGB"))
assert len(sources) == 3
source_index, row, checked = 0, 0, 0
for page in reader.pages:
    assert abs(float(page.mediabox.width) - 595.276) < 0.01
    assert abs(float(page.mediabox.height) - 841.89) < 0.01
    objects = page["/Resources"]["/XObject"]
    assert len(objects) == 1
    image = objects["/Capture"].get_object()
    width, height = int(image["/Width"]), int(image["/Height"])
    assert image["/Filter"] == "/FlateDecode"
    assert image["/BitsPerComponent"] == 8 and image["/ColorSpace"] == "/DeviceRGB"
    source = sources[source_index]
    assert width == source.width and row + height <= source.height
    expected = source.crop((0, row, width, row + height)).tobytes()
    assert image.get_data() == expected, f"Pérdida de píxeles en página {checked + 1}"
    row += height
    checked += 1
    if row == source.height:
        source_index += 1
        row = 0
assert source_index == len(sources) and row == 0
assert reader.trailer["/Root"]["/Pages"]["/Count"] == checked
print(f"PASS PDF independiente: {checked} páginas; filas, orden, censura y todos los píxeles RGB intactos.")
