"""The app icon: the editor's logo (gold rounded square, sound bars) as a multi-size .ico."""
from PIL import Image, ImageDraw

HI, LO, INK = (230, 189, 85), (184, 134, 28), (36, 25, 0)
BARS = [(2, 10, 13), (6, 6, 17), (10, 3, 21), (14, 8, 15), (18, 5, 18), (22, 10, 13)]   # lucide "audio-lines"


def logo(size):
    k = 4                                                   # draw large, then shrink: smooth edges
    n = size * k
    grad = Image.new("RGB", (n, n))
    px = grad.load()
    for y in range(n):
        for x in range(n):
            t = (x + y) / (2 * n - 2)
            px[x, y] = tuple(round(HI[i] + (LO[i] - HI[i]) * t) for i in range(3))
    mask = Image.new("L", (n, n), 0)
    pad = round(n * 0.03)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, n - pad - 1, n - pad - 1], radius=round(n * 0.25), fill=255)
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    box = n * 0.52                                          # the glyph's 24-unit box, centred
    off = (n - box) / 2
    u = box / 24
    w = max(u * (2.4 if size >= 48 else 3.2), 1)            # thicker bars at small sizes so they stay visible
    for x, y1, y2 in BARS:
        cx = off + x * u
        d.rounded_rectangle([cx - w / 2, off + y1 * u - w / 2, cx + w / 2, off + y2 * u + w / 2], radius=w / 2, fill=INK)
    return img.resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    sizes = [256, 128, 64, 48, 40, 32, 24, 20, 16]
    imgs = [logo(s) for s in sizes]
    imgs[0].save("app.ico", sizes=[(s, s) for s in sizes], append_images=imgs[1:])
    imgs[0].save("app-256.png")
    print("ok")
