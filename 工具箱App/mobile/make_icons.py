#!/usr/bin/env python3
"""生成 App 图标（工具箱造型：深色圆角底 + 三个格子 + 一个手柄）。"""
from PIL import Image, ImageDraw

BG = (17, 19, 24, 255)
ACC = (91, 140, 255, 255)
ACC2 = (122, 162, 255, 255)
WHITE = (232, 234, 240, 255)


def rounded(draw, box, r, fill):
    draw.rounded_rectangle(box, radius=r, fill=fill)


def make(size, maskable=False):
    ss = 4
    S = size * ss
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    pad = 0 if maskable else int(S * 0.055)
    rounded(d, (pad, pad, S - pad, S - pad), int(S * 0.22), BG)

    # 工具箱主体
    bx0, by0 = int(S * 0.20), int(S * 0.42)
    bx1, by1 = int(S * 0.80), int(S * 0.78)
    rounded(d, (bx0, by0, bx1, by1), int(S * 0.045), ACC)

    # 箱盖
    lx0, ly0 = int(S * 0.155), int(S * 0.335)
    lx1, ly1 = int(S * 0.845), int(S * 0.455)
    rounded(d, (lx0, ly0, lx1, ly1), int(S * 0.035), ACC2)

    # 手柄：圆角拱形（先画粗线再画细线挖空，模拟空心把手）
    hw = int(S * 0.048)
    hx0, hy0 = int(S * 0.375), int(S * 0.225)
    hx1, hy1 = int(S * 0.625), int(S * 0.355)
    d.arc((hx0, hy0, hx1, hy1), 180, 360, fill=ACC2, width=hw)
    d.line((hx0, hy1 - hw // 2, hx0, hy0 + (hy1 - hy0) // 2), fill=ACC2, width=hw)
    d.line((hx1, hy1 - hw // 2, hx1, hy0 + (hy1 - hy0) // 2), fill=ACC2, width=hw)

    # 锁扣
    lk0, lk1 = int(S * 0.455), int(S * 0.545)
    d.rectangle((lk0, int(S * 0.40), lk1, int(S * 0.50)), fill=BG)
    d.ellipse((lk0 + (lk1 - lk0) // 4, int(S * 0.425), lk1 - (lk1 - lk0) // 4, int(S * 0.475)),
              fill=WHITE)

    # 底部三个格子
    gy = int(S * 0.545)
    gh = int(S * 0.135)
    gap = int(S * 0.028)
    gw = (bx1 - bx0 - gap * 2) // 3
    for i in range(3):
        gx = bx0 + i * (gw + gap)
        rounded(d, (gx, gy, gx + gw, gy + gh), int(S * 0.018), WHITE)

    return img.resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    make(192).save("icons/icon-192.png")
    make(512).save("icons/icon-512.png")
    make(512, maskable=True).save("icons/icon-maskable-512.png")
    print("ok")
