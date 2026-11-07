import struct, math, os

S = 64
px = []
cx = cy = (S - 1) / 2.0
for y in range(S):
    row = []
    for x in range(S):
        dx, dy = x - cx, y - cy
        d = math.hypot(dx, dy)
        # rounded square mask
        r = 9.0
        qx = max(abs(dx) - (S / 2.0 - r), 0.0)
        qy = max(abs(dy) - (S / 2.0 - r), 0.0)
        inside = (math.hypot(qx, qy) - r) < 0
        if not inside:
            row.append((0, 0, 0, 0))
            continue
        # base dark panel
        bg = (26, 26, 26, 255)
        # aperture ring
        col = bg
        ring = abs(d - 19.0)
        if ring < 3.2:
            t = 1.0 - ring / 3.2
            a = (74, 158, 255)
            col = tuple(int(bg[i] + (a[i] - bg[i]) * min(1.0, t * 1.6)) for i in range(3)) + (255,)
        # aperture blades hint (6 wedges)
        if 4 < d < 17.5:
            ang = math.atan2(dy, dx)
            seg = (ang + math.pi) / (math.pi / 3.0)
            f = seg - math.floor(seg)
            if abs(f - 0.5) < 0.06:
                col = (60, 120, 200, 255)
        # center glow
        if d < 4.0:
            t = 1.0 - d / 4.0
            col = tuple(int(26 + (255 - 26) * t * 0.9) for _ in range(3)) + (255,)
            col = (int(26 + (170 - 26) * t), int(26 + (220 - 26) * t), 255, 255)
        row.append(col)
    px.append(row)

# BMP-based ICO (BGRA bottom-up rows + AND mask)
xor = bytearray()
for y in range(S - 1, -1, -1):
    for x in range(S):
        b, g, r, a = px[y][x][2], px[y][x][1], px[y][x][0], px[y][x][3]
        xor += bytes((b, g, r, a))
and_mask = bytearray()
rowbytes = ((S + 31) // 32) * 4
for y in range(S - 1, -1, -1):
    bits = bytearray(rowbytes)
    for x in range(S):
        if px[y][x][3] == 0:
            bits[x // 8] |= (0x80 >> (x % 8))
    and_mask += bits

bmp_header = struct.pack('<IiiHHIIiiII', 40, S, S * 2, 1, 32, 0, len(xor) + len(and_mask), 0, 0, 0, 0)
img = bmp_header + bytes(xor) + bytes(and_mask)
icondir = struct.pack('<HHH', 0, 1, 1)
entry = struct.pack('<BBBBHHII', S % 256, S % 256, 0, 0, 1, 32, len(img), 22)

out = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'app.ico')
with open(out, 'wb') as f:
    f.write(icondir + entry + img)
print('wrote', out, len(icondir + entry + img), 'bytes')
