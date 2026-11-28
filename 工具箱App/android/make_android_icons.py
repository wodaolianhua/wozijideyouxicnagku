#!/usr/bin/env python3
"""生成 Android 启动图标（各密度 PNG + 自适应图标前景）。"""
from PIL import Image, ImageDraw
import os

BG = (17, 19, 24, 255)
ACC = (91, 140, 255, 255)
ACC2 = (122, 162, 255, 255)
WHITE = (232, 234, 240, 255)

RES = os.path.join(os.path.dirname(__file__), "app", "src", "main", "res")

DENSITIES = {
    "mipmap-mdpi": 48,
    "mipmap-hdpi": 72,
    "mipmap-xhdpi": 96,
    "mipmap-xxhdpi": 144,
    "mipmap-xxxhdpi": 192,
}


def draw_icon(size, ss=4, fg_only=False):
    S = size * ss
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def rr(box, r, fill):
        d.rounded_rectangle(box, radius=r, fill=fill)

    if fg_only:
        # 自适应图标前景：内容缩到中间 ~62%，四周留白给系统裁切
        pad = int(S * 0.19)
        bx0, by0 = pad + int((S - 2 * pad) * 0.20), pad + int((S - 2 * pad) * 0.42)
        bx1, by1 = pad + int((S - 2 * pad) * 0.80), pad + int((S - 2 * pad) * 0.78)
        lx0, ly0 = pad + int((S - 2 * pad) * 0.155), pad + int((S - 2 * pad) * 0.335)
        lx1, ly1 = pad + int((S - 2 * pad) * 0.845), pad + int((S - 2 * pad) * 0.455)
        hw = int(S * 0.045)
        hx0, hy0 = pad + int((S - 2 * pad) * 0.375), pad + int((S - 2 * pad) * 0.225)
        hx1, hy1 = pad + int((S - 2 * pad) * 0.625), pad + int((S - 2 * pad) * 0.355)
        body_c, lid_c = ACC, ACC2
    else:
        bx0, by0 = int(S * 0.20), int(S * 0.42)
        bx1, by1 = int(S * 0.80), int(S * 0.78)
        lx0, ly0 = int(S * 0.155), int(S * 0.335)
        lx1, ly1 = int(S * 0.845), int(S * 0.455)
        hw = int(S * 0.048)
        hx0, hy0 = int(S * 0.375), int(S * 0.225)
        hx1, hy1 = int(S * 0.625), int(S * 0.355)
        body_c, lid_c = ACC, ACC2

    # 手柄（拱形）
    d.arc((hx0, hy0, hx1, hy1), 180, 360, fill=lid_c, width=hw)
    d.line((hx0, hy1 - hw // 2, hx0, hy0 + (hy1 - hy0) // 2), fill=lid_c, width=hw)
    d.line((hx1, hy1 - hw // 2, hx1, hy0 + (hy1 - hy0) // 2), fill=lid_c, width=hw)

    # 箱体
    rr((bx0, by0, bx1, by1), int(S * 0.045), body_c)
    # 箱盖
    rr((lx0, ly0, lx1, ly1), int(S * 0.035), lid_c)

    # 锁扣
    lk0, lk1 = int(S * 0.455), int(S * 0.545)
    d.rectangle((lk0, int(S * 0.40), lk1, int(S * 0.50)), fill=BG if not fg_only else (17, 19, 24, 255))
    d.ellipse((lk0 + (lk1 - lk0) // 4, int(S * 0.425), lk1 - (lk1 - lk0) // 4, int(S * 0.475)), fill=WHITE)

    # 底部格子
    gy = int(S * 0.545)
    gh = int(S * 0.135)
    gap = int(S * 0.028)
    gw = (bx1 - bx0 - gap * 2) // 3
    for i in range(3):
        gx = bx0 + i * (gw + gap)
        rr((gx, gy, gx + gw, gy + gh), int(S * 0.018), WHITE)

    return img.resize((size, size), Image.LANCZOS)


def main():
    for folder, size in DENSITIES.items():
        out = os.path.join(RES, folder)
        os.makedirs(out, exist_ok=True)
        draw_icon(size).save(os.path.join(out, "ic_launcher.png"))
        draw_icon(size).save(os.path.join(out, "ic_launcher_round.png"))
        # 自适应图标前景：108dp 画布，xxhdpi 为 432
        fg_size = int(size * 108 / 48)
        draw_icon(fg_size, fg_only=True).save(os.path.join(out, "ic_launcher_foreground.png"))
    print("android icons ok")


if __name__ == "__main__":
    main()
