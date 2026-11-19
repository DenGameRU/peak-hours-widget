"""Generates the placeholder PNG assets used by the MSIX package.

Pure-stdlib PNG writer so it works without Pillow. Run:
    python tools/generate_assets.py
"""

import math
import os
import struct
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "src", "PeakHoursWidget", "Assets")
PROVIDER_ASSETS = os.path.join(ROOT, "src", "PeakHoursWidget", "ProviderAssets")


def write_png(path, width, height, pixel_fn):
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filter: none
        for x in range(width):
            raw.extend(pixel_fn(x, y, width, height))

    def chunk(tag, data):
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        f.write(chunk(b"IEND", b""))


def icon_pixel(x, y, w, h):
    cx, cy = (w - 1) / 2.0, (h - 1) / 2.0
    radius = math.hypot(x - cx, y - cy) / max(1.0, min(w, h) / 2.0)
    t = y / max(1, h - 1)
    bg = (int(20 + 18 * t), int(34 + 28 * t), int(66 + 40 * t), 255)

    if radius > 0.94:
        return (int(bg[0] * 0.5), int(bg[1] * 0.5), int(bg[2] * 0.5), 255)
    if 0.79 <= radius <= 0.94:
        return (232, 239, 252, 255)
    if radius < 0.68:
        angle = math.atan2(y - cy, x - cx)
        minute_delta = abs(((angle + math.pi / 2 + math.pi) % (2 * math.pi)) - math.pi)
        hour_delta = abs(((angle + math.pi / 6 + math.pi) % (2 * math.pi)) - math.pi)
        if (minute_delta < 0.13 and radius < 0.56) or (
            hour_delta < 0.17 and radius < 0.40
        ):
            return (255, 199, 74, 255)
        return (244, 248, 255, 255)
    return bg


def screenshot_pixel(x, y, w, h):
    bg = (30, 34, 46, 255)
    if 18 <= x < w - 18 and 14 <= y < h - 14:
        panel = (45, 51, 68, 255)
        if 34 <= y < 56 and 34 <= x < w - 70:
            panel = (236, 241, 252, 255)
        if 64 <= y < 76 and 34 <= x < w - 90:
            panel = (145, 156, 180, 255)
        if 88 <= y < 114 and 34 <= x < 150:
            panel = (64, 174, 122, 255)
        for row_y in (134, 164):
            if row_y <= y < row_y + 10:
                if 34 <= x < 210:
                    panel = (158, 168, 190, 255)
                if w - 170 <= x < w - 34:
                    panel = (236, 241, 252, 255)
        return panel
    return bg


def main():
    write_png(os.path.join(ASSETS, "StoreLogo.png"), 50, 50, icon_pixel)
    write_png(os.path.join(ASSETS, "Square44x44Logo.png"), 44, 44, icon_pixel)
    write_png(
        os.path.join(ASSETS, "Square44x44Logo.scale-200.png"), 88, 88, icon_pixel
    )
    write_png(
        os.path.join(ASSETS, "Square44x44Logo.targetsize-24_altform-unplated.png"),
        24,
        24,
        icon_pixel,
    )
    write_png(
        os.path.join(ASSETS, "Square150x150Logo.scale-200.png"),
        300,
        300,
        icon_pixel,
    )
    write_png(
        os.path.join(ASSETS, "Wide310x150Logo.scale-200.png"), 620, 300, icon_pixel
    )
    write_png(os.path.join(ASSETS, "LockScreenLogo.scale-200.png"), 48, 48, icon_pixel)
    write_png(
        os.path.join(ASSETS, "SplashScreen.scale-200.png"), 1240, 600, icon_pixel
    )
    write_png(
        os.path.join(PROVIDER_ASSETS, "PeakHours_Icon.png"), 100, 100, icon_pixel
    )
    write_png(
        os.path.join(PROVIDER_ASSETS, "PeakHours_Screenshot.png"),
        400,
        200,
        screenshot_pixel,
    )
    print("Assets generated.")


if __name__ == "__main__":
    main()
