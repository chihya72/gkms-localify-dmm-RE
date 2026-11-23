"""Read-only TMP metadata / machine-code inspection; never loads or launches the game."""
import argparse
import struct
from pathlib import Path

import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64


def inspect(game):
    data = (game / "gakumas_Data" / "il2cpp_data" / "Metadata" / "global-metadata.dat").read_bytes()
    header = struct.unpack_from("<64I", data)
    assert header[0] == 0xFAB11BAF and header[1] == 31, "This reader supports metadata v31 only"
    strings, methods, types = header[6], header[12], header[40]

    def string(index):
        start = strings + index
        return data[start:data.index(0, start)].decode("utf-8")

    pe = pefile.PE(str(game / "GameAssembly.dll"), fast_load=True)
    binary = pe.__data__
    base = pe.OPTIONAL_HEADER.ImageBase
    module_name = binary.find(b"Unity.TextMeshPro.dll\0")
    if module_name < 0:
        print("Codegen module names are not readable in the on-disk image; reporting metadata only (no native-code conclusion).")
        assert header[13] % 36 == 0 and header[41] % 88 == 0
        selected = {}
        for index in range(header[41] // 88):
            name, namespace = struct.unpack_from("<ii", data, types + index * 88)
            if string(namespace) == "TMPro" and string(name) in ("TMP_Text", "TMP_FontAsset", "TMP_Asset", "TextMeshProUGUI", "TextMeshPro"):
                selected[index] = string(name)
        for index in range(header[13] // 36):
            row = struct.unpack_from("<7I4H", data, methods + index * 36)
            name, owner, _, _, _, _, token, _, _, _, arity = row
            if owner in selected and (string(name) in ("SetText", "set_text", "PopulateTextBackingArray", "SetCharArray", "Awake", "OnPreRenderCanvas", "OnPreRenderObject", "CreateFontAsset") or any(key in string(name) for key in ("faceInfo", "fontWeightTable", "fallbackFontAssetTable", "atlasPadding", "atlasRenderMode"))):
                print(f"{selected[owner]}.{string(name)}/{arity} token={token:#x}")
        return
    name_va = base + pe.get_rva_from_offset(module_name)
    references = []
    start = 0
    while (start := binary.find(struct.pack("<Q", name_va), start)) >= 0:
        count, pointers = struct.unpack_from("<QQ", binary, start + 8)
        if 100 < count < 20000 and base <= pointers < base + pe.OPTIONAL_HEADER.SizeOfImage:
            references.append((count, pointers))
        start += 8
    assert len(references) == 1, references
    count, pointers = references[0]
    pointers_offset = pe.get_offset_from_rva(pointers - base)
    print(f"Metadata v31; TMP codegen methods={count}; image base={base:#x}")
    # v31 includes returnParameterToken; type definitions retain elementTypeIndex.
    assert header[13] % 36 == 0 and header[41] % 88 == 0
    selected = {}
    for index in range(header[41] // 88):
        offset = types + index * 88
        name, namespace = struct.unpack_from("<ii", data, offset)
        if string(namespace) == "TMPro" and string(name) in ("TMP_Text", "TMP_FontAsset", "TMP_Asset", "TextMeshProUGUI", "TextMeshPro"):
            selected[index] = string(name)
    print("Types:", selected)
    disassembler = Cs(CS_ARCH_X86, CS_MODE_64)
    for index in range(header[13] // 36):
        row = struct.unpack_from("<7I4H", data, methods + index * 36)
        name, owner, _, _, _, _, token, _, _, _, arity = row
        if owner not in selected:
            continue
        name = string(name)
        if name not in ("SetText", "set_text", "PopulateTextBackingArray", "SetCharArray", "Awake", "OnPreRenderCanvas", "OnPreRenderObject", "CreateFontAsset", "get_faceInfo", "set_faceInfo"):
            continue
        address = struct.unpack_from("<Q", binary, pointers_offset + ((token & 0xFFFFFF) - 1) * 8)[0]
        print(f"{selected[owner]}.{name}/{arity} token={token:#x} RVA={address-base:#x}")
        if selected[owner] == "TMP_Text" and name in ("SetText", "set_text", "PopulateTextBackingArray") and arity <= 3:
            code = pe.get_data(address - base, 320)
            for instruction in disassembler.disasm(code, address):
                print(f"  {instruction.address-base:08x} {instruction.mnemonic:8} {instruction.op_str}")
                if instruction.mnemonic == "ret":
                    break


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("game", type=Path)
    inspect(parser.parse_args().game)
