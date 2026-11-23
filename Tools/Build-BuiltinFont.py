"""Build the fixed embedded Unity Font bundle offline; does not start Unity or the game."""
import argparse
from io import BytesIO
from pathlib import Path

import UnityPy
from fontTools.ttLib import TTFont

parser = argparse.ArgumentParser()
parser.add_argument('--template', required=True)
parser.add_argument('--font', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
font_bytes = Path(args.font).read_bytes()
font = TTFont(BytesIO(font_bytes))
env = UnityPy.load(args.template)
fonts = [obj for obj in env.objects if obj.type.name == 'Font']
if len(fonts) != 1:
    raise RuntimeError('Expected one Font object in the bundle template')
tree = fonts[0].read_typetree()
tree['m_FontData'] = list(font_bytes)
tree['m_Name'] = 'gkamsZHFontMIX-GameComplete'
tree['m_FontNames'] = ['gkamsZHFontMIX Game Complete']
fonts[0].save_typetree(tree)
# Preserve Unity serialization, internal asset path, material and texture objects.
# No additional directories or editor import are needed.
output = Path(args.output)
output.write_bytes(next(iter(env.files.values())).save(packer='lz4'))
check = UnityPy.load(str(output))
loaded = [bytes(obj.read().m_FontData) for obj in check.objects if obj.type.name == 'Font']
assert loaded == [font_bytes], 'Serialized font differs from the selected OTF'
assert 'assets/fonts/gkamszhfontmix.otf' in check.container
print(f'Embedded bundle verified: {len(font.getBestCmap())} code points; {output.stat().st_size} bytes; {output}')
