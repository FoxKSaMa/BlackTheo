from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent / "img"
OUT.mkdir(parents=True, exist_ok=True)

FONT = "C:/Windows/Fonts/msyh.ttc"
FONT_BOLD = "C:/Windows/Fonts/msyhbd.ttc"


def font(size, bold=False):
    path = FONT_BOLD if bold else FONT
    return ImageFont.truetype(path, size)


def rounded(draw, box, fill, outline, radius=18, width=2):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def arrow(draw, start, end, fill, width=4, head=14):
    draw.line([start, end], fill=fill, width=width)
    x1, y1 = start
    x2, y2 = end
    import math
    angle = math.atan2(y2 - y1, x2 - x1)
    p1 = (x2 - head * math.cos(angle - 0.45), y2 - head * math.sin(angle - 0.45))
    p2 = (x2 - head * math.cos(angle + 0.45), y2 - head * math.sin(angle + 0.45))
    draw.polygon([end, p1, p2], fill=fill)


def diagram_pipeline():
    W, H = 1500, 640
    img = Image.new("RGB", (W, H), "#f7f8fb")
    d = ImageDraw.Draw(img)

    d.text((60, 40), "BlackTheo 的渲染插入点", font=font(40, True), fill="#172033")
    d.text((60, 98), "Celeste 先把整个世界画进 320×180 的 GameplayBuffers.Level，最后才放大到屏幕并画 HUD。",
           font=font(23), fill="#4a5568")

    boxes = [
        ("背景 / Backdrop", 60, 190, 210, 90, "#e8eefc", "#8aa4d8"),
        ("Gameplay", 300, 190, 180, 90, "#e8eefc", "#8aa4d8"),
        ("Lighting", 510, 190, 160, 90, "#e8eefc", "#8aa4d8"),
        ("Foreground", 700, 190, 180, 90, "#e8eefc", "#8aa4d8"),
        ("Glitch.Apply", 910, 175, 220, 120, "#fff1d6", "#e0a43b"),
        ("HUD", 1210, 190, 140, 90, "#e8eefc", "#8aa4d8"),
    ]
    for text, x, y, w, h, fill, outline in boxes:
        rounded(d, (x, y, x + w, y + h), fill, outline)
        tw = d.textlength(text, font=font(24, True))
        d.text((x + (w - tw) / 2, y + 28), text, font=font(24, True), fill="#172033")

    for i in range(len(boxes) - 1):
        x1 = boxes[i][1] + boxes[i][3]
        y1 = boxes[i][2] + boxes[i][4] / 2
        x2 = boxes[i + 1][1]
        arrow(d, (x1 + 10, y1), (x2 - 10, y1), "#7c8aa5", width=3, head=10)

    d.text((910, 315), "我们的 Hook 就挂在这里", font=font(22, True), fill="#b36b00")
    d.text((910, 350), "此时背景、角色、前景都已合成进 Level 目标；", font=font(19), fill="#6b7280")
    d.text((910, 380), "HUD 还没有绘制。", font=font(19), fill="#6b7280")

    d.text((60, 340), "Level.Render() 的渲染顺序（简化）", font=font(22, True), fill="#172033")

    # Ping-pong explanation
    top = 450
    d.text((60, top - 45), "一个像素不能同时被读取和写入：用离屏目标做 ping-pong", font=font(24, True), fill="#172033")

    rounded(d, (70, top, 420, top + 120), "#e8eefc", "#7189b8")
    d.text((100, top + 24), "GameplayBuffers.Level", font=font(23, True), fill="#172033")
    d.text((100, top + 60), "当前世界画面 A（320×180）", font=font(19), fill="#4a5568")

    rounded(d, (930, top, 1290, top + 120), "#eef7ee", "#6fa574")
    d.text((960, top + 24), "capture", font=font(23, True), fill="#172033")
    d.text((960, top + 60), "临时 RenderTarget B", font=font(19), fill="#4a5568")

    arrow(d, (435, top + 40), (555, top + 40), "#3c6db3", width=5)
    d.text((430, top + 2), "① Copy A → B", font=font(20, True), fill="#3c6db3")
    arrow(d, (560, top + 95), (435, top + 95), "#2f8f5b", width=5)
    d.text((450, top + 105), "② Shader 读 B，写回 A", font=font(20, True), fill="#2f8f5b")

    d.text((70, 600), "这样每个屏幕像素都可以读取邻域/整张画面，再决定自己的新颜色；HUD 在之后才画，因此不会被吞进黑洞。",
           font=font(18), fill="#4a5568")

    img.save(OUT / "render-pipeline.png")
    return OUT / "render-pipeline.png"


def diagram_ray():
    W, H = 1500, 860
    img = Image.new("RGB", (W, H), "#070b16")
    d = ImageDraw.Draw(img)

    d.text((55, 38), "光线追踪与吸积盘形状", font=font(42, True), fill="#e8f1ff")
    d.text((55, 96), "不是把椭圆贴上去：先积分光线，再记录它与 y=0 盘面的交点。",
           font=font(23), fill="#9fb2cf")

    # Disk plane (perspective ellipse)
    cx, cy = 820, 430
    for i, (rx, ry, col, width) in enumerate([
        (480, 118, (20, 52, 112), 3),
        (430, 100, (40, 92, 180), 3),
        (350, 78, (38, 120, 220), 4),
    ]):
        d.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), outline=col, width=width)

    # Atmospheric disk glow
    for i in range(14, 0, -1):
        alpha = int(10 * (15 - i) / 14)
        col = (20 + alpha, 60 + 3 * alpha, 150 + 4 * alpha)
        d.ellipse((cx - 460, cy - 108, cx + 460, cy + 108), outline=col, width=3)

    # Black hole
    bh_r = 92
    for i in range(10, 0, -1):
        r = bh_r + i * 4
        d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=(10 + i, 40 + 4 * i, 80 + 7 * i), width=4)

    d.ellipse((cx - bh_r, cy - bh_r, cx + bh_r, cy + bh_r), fill="#000000", outline="#9edbff", width=3)
    d.ellipse((cx - bh_r - 5, cy - bh_r - 5, cx + bh_r + 5, cy + bh_r + 5), outline="#2b6fb5", width=2)

    # Near side disk band (front, diagonal)
    d.line((cx - 460, cy + 18, cx + 460, cy - 38), fill="#8fdcff", width=5)
    d.line((cx - 460, cy + 27, cx + 460, cy - 29), fill="#1d5ca8", width=3)

    # Far side lensed arcs above/below the shadow
    d.arc((cx - 290, cy - 250, cx + 290, cy + 250), start=205, end=335, fill="#7fd0ff", width=14)
    d.arc((cx - 270, cy - 225, cx + 270, cy + 225), start=200, end=340, fill="#2d7fce", width=8)
    d.arc((cx - 300, cy - 260, cx + 300, cy + 260), start=25, end=155, fill="#7fd0ff", width=14)
    d.arc((cx - 275, cy - 235, cx + 275, cy + 235), start=20, end=160, fill="#2d7fce", width=8)

    # Camera and rays
    cam = (160, 610)
    d.ellipse((cam[0] - 18, cam[1] - 18, cam[0] + 18, cam[1] + 18), fill="#e6eefc", outline="#7da1d4", width=3)
    d.text((95, 640), "camera", font=font(20), fill="#9fb2cf")

    ray_color = "#6ec7ff"
    # Straight near-side ray
    d.line((cam[0] + 20, cam[1] - 20, cx - 380, cy + 40), fill=ray_color, width=2)
    d.line((cx - 380, cy + 40, cx + 380, cy + 40), fill=ray_color, width=2)

    # Bent far-side ray: approximate with polyline
    d.line((cam[0] + 20, cam[1] - 30, 560, 330, 610, 250), fill=ray_color, width=2)
    d.line((610, 250, 700, 190, 820, 170, 950, 210, 1040, 300), fill=ray_color, width=2)
    d.line((cam[0] + 20, cam[1] - 40, 560, 540, 610, 610), fill=ray_color, width=2)
    d.line((610, 610, 700, 650, 820, 665, 950, 620, 1040, 540), fill=ray_color, width=2)

    # Labels
    def label(x, y, text, color="#dbe7ff", size=21, anchor=None):
        d.text((x, y), text, font=font(size, True), fill=color, anchor=anchor)

    label(655, 128, "远侧盘面被弯到上方", "#9ee2ff")
    d.line((660, 158, 760, 220), fill="#31618d", width=2)
    label(655, 720, "远侧盘面被弯到下方", "#9ee2ff")
    d.line((660, 698, 760, 630), fill="#31618d", width=2)
    label(300, 455, "近侧盘面：从黑洞前方穿过", "#c9f1ff")
    d.line((520, 445, 615, 455), fill="#31618d", width=2)
    label(700, 505, "事件视界 / 阴影", "#eaf6ff", 22)
    d.line((800, 495, 820, 500), fill="#31618d", width=2)
    label(1015, 350, "吸积盘平面 y = 0", "#9fb2cf", 20)
    d.line((1000, 370, 930, 420), fill="#31618d", width=2)

    d.text((55, 770), "每条像素光线最多记录 3 次盘面交点；r < 1 表示被黑洞捕获。",
           font=font(19), fill="#7f92ad")
    d.text((55, 802), "近侧交点在阴影前 → 细带；远侧交点被偏折到阴影上下 → 弧。",
           font=font(19), fill="#7f92ad")

    img.save(OUT / "ray-geometry.png")
    return OUT / "ray-geometry.png"


if __name__ == "__main__":
    print(diagram_pipeline())
    print(diagram_ray())
