"""Build In-Sight clipboard payloads for the DATA MAP / CALIB MAP template blocks.

In-Sight's clipboard format "Cognex.InSight.Internal.CvsCellClipboardData" is a
.NET BinaryFormatter stream holding one byte[] with CopyBufferObject JSON.
Outputs (next to this script):
  maps_block.bin : paste at K21 (fills K21:Z41)
  maps_q2.bin    : paste at Q2 (output buffer = Result + 16 data slots)
"""
import json
import os
import struct

HERE = os.path.dirname(os.path.abspath(__file__))


def _lps(s):
    b = s.encode("ascii")
    return bytes([len(b)]) + b


# Header bytes of a real In-Sight copy: .NET DataObject serialized-object GUID,
# then BinaryFormatter record of CvsCellClipboardData { byte[] qbhr3EuH3I }.
HEADER = (bytes.fromhex("96a79efd133b7043a67956106bb288fb")
          + bytes([0x00]) + struct.pack("<iiii", 1, -1, 1, 0)
          + bytes([0x0C]) + struct.pack("<i", 2)
          + _lps("Cognex.InSight, Version=1.0.0.0, Culture=neutral, PublicKeyToken=8435c339e4fd2557")
          + bytes([0x05]) + struct.pack("<i", 1) + _lps("Cognex.InSight.Internal.CvsCellClipboardData")
          + struct.pack("<i", 1) + _lps("qbhr3EuH3I")
          + bytes([0x07, 0x02]) + struct.pack("<i", 2)
          + bytes([0x09]) + struct.pack("<i", 3)
          + bytes([0x0F]) + struct.pack("<i", 3))

# Prefer the header of a real copy from the target In-Sight version (DumpClip.ps1).
_raw = os.path.join(HERE, "clip_raw.bin")
if os.path.exists(_raw):
    _s = open(_raw, "rb").read()
    HEADER = _s[:_s.index(b'{"$type":"CopyBufferObject"') - 5]

ST_HEAD = '.cell {color:rgba(255,255,255,1.0); background-color:rgba(0,0,255,1.0); font: bold "Arial" 9pt ; }'
ST_LABEL = '.cell {font: bold "Arial" 9pt ; }'
ST_INPUT = '.cell {background-color:rgba(255,255,0,1.0); text-align: center; font: bold "Arial" 9pt ; }'
ST_EDIT = '.cell {color:rgba(0,0,0,1.0); background-color:rgba(255,255,255,1.0); text-align: center; font: bold "Arial" 9pt ; }'
ST_CALC = '.cell {color:rgba(0,0,0,1.0); background-color:rgba(255,255,255,1.0); number-format: "0.000"; text-align: center; font: bold "Arial" 9pt ; }'


def cell(addr, expr, style="", value=0.0, state=None, comment=""):
    if expr.startswith("'"):
        value = expr[1:]
    return [addr, expr, 1, value, "", state, style, "", comment, 0, 0, 0]


def edit_int(addr, lo, hi, val, comment=""):
    return cell(addr, f"EditInt({lo},{hi})", ST_EDIT, val, {"max": hi, "min": lo, "value": val}, comment)


def edit_float(addr, lo, hi, val=0.0):
    return cell(addr, f"EditFloat({lo},{hi})", ST_EDIT, val, {"max": hi, "min": lo, "value": val})


def block_cells():
    c = []
    # DATA MAP K21:P37
    for col, text in zip("KLMNOP", ["DATA MAP", "Name", "Source", "Scale", "Out", "Valid"]):
        c.append(cell(f"{col}21", "'" + text, ST_HEAD))
    for i in range(1, 17):
        r = 21 + i
        c.append(cell(f"K{r}", "'" + str(i), ST_LABEL))
        c.append(cell(f"L{r}", "'Data%02d" % i, ST_LABEL))
        c.append(cell(f"M{r}", "0", ST_INPUT, comment="Source: replace 0 with a cell reference, e.g. =$X$34"))
        c.append(cell(f"N{r}", "1", ST_INPUT, 1.0, comment="Scale: Out = Source * Scale"))
        c.append(cell(f"O{r}", f"If(ErrFree($M${r}),$M${r}*$N${r},0)", ST_CALC))
        c.append(cell(f"P{r}", f"If(ErrFree($M${r}),1,0)", ST_CALC))

    # CALIB MAP S21:Z41
    c.append(cell("S21", "'CALIB MAP", ST_HEAD))
    c.append(cell("T21", "'Mode", ST_HEAD))
    c.append(edit_int("U21", 0, 1, 0, "0=Pixel (no calibration), 1=World (Calibrate at T32)"))
    c.append(cell("V21", "'CalOK", ST_HEAD))
    c.append(cell("W21", "If(ErrFree($T$32),1,0)", ST_CALC))
    c.append(cell("X21", "'0=Pixel 1=World", ST_LABEL))
    for col, text in zip("STUVW", ["Pt", "PixX", "PixY", "WldX", "WldY"]):
        c.append(cell(f"{col}22", "'" + text, ST_HEAD))
    for i in range(1, 10):
        r = 22 + i
        c.append(cell(f"S{r}", "'" + str(i), ST_LABEL))
        for col in "TUVW":
            c.append(edit_float(f"{col}{r}", -99999, 99999))
    c.append(cell("S32", "'Calib", ST_HEAD))
    c.append(cell("U32", "'<- T32: insert Calibrate (points T23:W31)", ST_LABEL))
    for col, text in zip("STUVWXYZ", ["SrcX", "SrcY", "SrcAng", "WldPt", "WldPt2", "OutX", "OutY", "OutAng"]):
        c.append(cell(f"{col}33", "'" + text, ST_HEAD))
    for r in range(34, 42):
        for col in "STU":
            c.append(cell(f"{col}{r}", "0", ST_INPUT, comment="Pixel input: replace 0 with a tool result reference"))
        c.append(cell(f"V{r}", f"TransPixelToWorld($A$0,$T$32,$S${r},$T${r},0)", ST_CALC))
        c.append(cell(f"W{r}", f"TransPixelToWorld($A$0,$T$32,$S${r}+100*Cos($U${r}),$T${r}+100*Sin($U${r}),0)", ST_CALC))
        c.append(cell(f"X{r}", f"If($U$21=1,GetX($V${r}),$S${r})", ST_CALC))
        c.append(cell(f"Y{r}", f"If($U$21=1,GetY($V${r}),$T${r})", ST_CALC))
        c.append(cell(f"Z{r}", f"If($U$21=1,Atan2(GetY($W${r})-GetY($V${r}),GetX($W${r})-GetX($V${r})),$U${r})", ST_CALC))
    return c


def q2_cells():
    fmt = ":".join(["il"] * 17)
    vals = ",".join(f"$O${r}" for r in range(22, 38))
    return [cell("Q2", f'FormatOutputBuffer(0,"{fmt}",$R$4,{vals})', ST_HEAD, {"$type": "FORMAT_OUT_DATA"})]


def write_clip(name, rng, cells):
    payload = json.dumps({"$type": "CopyBufferObject", "range": rng, "cells": cells},
                         ensure_ascii=False, separators=(",", ":")).encode("utf8")
    out = HEADER + struct.pack("<i", len(payload)) + bytes([2]) + payload + bytes([0x0B])
    open(os.path.join(HERE, name), "wb").write(out)
    print(name, rng, len(cells), "cells", len(out), "bytes")


def coef_cells():
    """Robot->image Jacobian from the jog captures (rows 19-24), paste at W18."""
    c = [cell("W18", "'Coef", ST_HEAD), cell("X18", "'Value", ST_HEAD)]
    rows = [
        ("a dPx/RX", "($P$21-$P$20)/($S$21-$S$20)"),
        ("c dPy/RX", "($Q$21-$Q$20)/($S$21-$S$20)"),
        ("b dPx/RY", "($P$23-$P$22)/($T$23-$T$22)"),
        ("d dPy/RY", "($Q$23-$Q$22)/($T$23-$T$22)"),
        ("det", "$X$19*$X$22-$X$21*$X$20"),
        ("kTheta", "If($U$24=0,1,($R$24-$R$19)/$U$24)"),
    ]
    for i, (label, expr) in enumerate(rows):
        c.append(cell(f"W{19 + i}", "'" + label, ST_LABEL))
        c.append(cell(f"X{19 + i}", expr, ST_CALC))
    return c


def out_cells():
    """Pixel delta -> robot mm/deg, paste at O28."""
    return [
        cell("O28", "'dPix", ST_HEAD),
        cell("P28", "$P$27-$P$26", ST_CALC),
        cell("Q28", "$Q$27-$Q$26", ST_CALC),
        cell("R28", "$R$27-$R$26", ST_CALC),
        cell("O29", "'Robot", ST_HEAD),
        cell("P29", "($X$22*$P$28-$X$21*$Q$28)/$X$23", ST_CALC),
        cell("Q29", "(-$X$20*$P$28+$X$19*$Q$28)/$X$23", ST_CALC),
        cell("R29", "$R$28/$X$24", ST_CALC),
        cell("O30", "'Corr", ST_HEAD),
        cell("P30", "-$P$29", ST_CALC),
        cell("Q30", "-$Q$29", ST_CALC),
        cell("R30", "-$R$29", ST_CALC),
    ]


BUTTON_STATE = {"$type": "Byte[]", "sz": 28, "base64": "AAAAAAAAAAAAAAAAAAAAAAAAAAABAAAAAAAAAA=="}
# 003_test layout: Setup table rows 22-41 (H = IsNow), point blocks from row 55 every 18 rows,
# point X/Y/Theta at I/J/K of block row +1, calibration Origin row L45:N45.
SETUP_FIRST, POINTS, BLOCK_FIRST, BLOCK_STRIDE = 22, 20, 55, 18
SETUP_LAST = SETUP_FIRST + POINTS - 1


def origin_table_cells():
    """Per-point origin table next to Setup (J21:M41). Row button stores that point's own X/Y/Theta."""
    c = [cell(f"{col}{SETUP_FIRST - 1}", "'" + t, ST_HEAD) for col, t in zip("JKLM", ["Teach", "OrgX", "OrgY", "OrgT"])]
    for k in range(POINTS):
        r = SETUP_FIRST + k
        src_row = BLOCK_FIRST + BLOCK_STRIDE * k + 1
        c.append(cell(f"J{r}", 'Button("",-1)', ST_EDIT, 0, BUTTON_STATE, "Teach: store this point's X/Y/Theta as origin"))
        for col, src in zip("KLM", "IJK"):
            x = cell(f"{col}{r}", f"${src}${src_row}", ST_CALC)
            x[2] = f"$J${r}"         # enable cell: value only updates when this row's button is pressed
            c.append(x)
    return c


def origin_select_cells():
    """Calibration Origin row L45:N45 = origin of the current point."""
    rng = f"$H${SETUP_FIRST}:$H${SETUP_LAST}"
    return [cell(f"{col}45", f"If(Sum({rng})=0,0,Choose(MaxI({rng}),${src}${SETUP_FIRST}:${src}${SETUP_LAST}))", ST_CALC)
            for col, src in zip("LMN", "KLM")]


write_clip("origin_table.bin", f"J{SETUP_FIRST - 1}:M{SETUP_LAST}", origin_table_cells())
write_clip("origin_select.bin", "L45:N45", origin_select_cells())
write_clip("calib_coef.bin", "W18:X24", coef_cells())
write_clip("calib_out.bin", "O28:R30", out_cells())
write_clip("maps_block.bin", "K21:Z41", block_cells())
write_clip("maps_q2.bin", "Q2:Q2", q2_cells())
