from pathlib import Path
from io import BytesIO
from PIL import Image
from pypdf import PdfReader

base = Path(__file__).parent
resources = PdfReader(base / "suite.pdf").pages[0]["/Resources"]["/XObject"]
paths = sorted(p for p in (base / "pngsuite").iterdir() if p.suffix in (".png", ".jpg"))
for object_id, path in enumerate(paths, start=3):
    obj = resources[f"/Im{object_id}"].get_object()
    if path.suffix == ".jpg":
        assert Image.open(BytesIO(obj.get_data())).convert("RGB").tobytes() == Image.open(path).convert("RGB").tobytes(), path.name
        print(path.name, "OK")
        continue
    mode = "L" if obj["/ColorSpace"] == "/DeviceGray" else "RGB"
    image = Image.frombytes(mode, (obj["/Width"], obj["/Height"]), obj.get_data()).convert("RGBA")
    if "/SMask" in obj:
        mask = obj["/SMask"].get_object()
        image.putalpha(Image.frombytes("L", image.size, mask.get_data()))
    expected = Image.open(path).convert("RGBA")
    assert image.tobytes() == expected.tobytes(), path.name
    print(path.name, "OK")
