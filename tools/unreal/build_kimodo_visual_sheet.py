"""Assemble aligned UE viewport captures into a compact review sheet."""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


directory = Path(__file__).resolve().parents[2] / "assets/generated/kimodo_ue/visual"
frames = (0, 30, 60, 90, 120)
root_y = (0.0, 118.2786, 235.5052, 325.5373, 346.3949)
crop = (250, 265, 1500, 780)
cell_width = 900
cell_height = 405
image_height = 371
sheet = Image.new("RGB", (3 * cell_width, 2 * cell_height), "#101519")
draw = ImageDraw.Draw(sheet)
font_path = Path("C:/Windows/Fonts/segoeui.ttf")
font = ImageFont.truetype(str(font_path), 23) if font_path.is_file() else ImageFont.load_default()

for index, (frame, displacement) in enumerate(zip(frames, root_y)):
    source = directory / f"pie_fbx_wide_f{frame:03d}.png"
    with Image.open(source) as original:
        image = original.convert("RGB").crop(crop)
        image = image.resize((cell_width, image_height), Image.Resampling.LANCZOS)
    x = (index % 3) * cell_width if index < 3 else (index - 3) * cell_width + cell_width // 2
    y = (index // 3) * cell_height
    sheet.paste(image, (x, y + 34))
    draw.text((x + 14, y + 4), f"{frame / 30:.0f} s  |  root +Y {displacement:.1f} cm",
              fill="#f0f3f5", font=font)

sheet.save(directory / "pie_fbx_5frame_contact.png", optimize=True)
