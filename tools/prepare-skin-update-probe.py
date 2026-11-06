"""Prepare an isolated read-only probe using the existing Windows message bridge.

The diagnostic dispatch has no skin/camera write commands. Separate protocol,
message and mapping names avoid resident production DLLs consuming requests.
"""
from pathlib import Path
import argparse
import re, shutil

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--effects', action='store_true', help='Build a read-only skin resource cache probe instead of the executable-code probe')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
source = root / 'native/LeagueSkinCore'
dest = root / ('.artifacts/skin-effects-probe' if args.effects else 'tmp/skin-update-probe')
native = dest / 'native'
native.mkdir(parents=True, exist_ok=True)
for path in (source / 'native').glob('*.h'):
    shutil.copy2(path, native / path.name)
shutil.copy2(source / 'build.ps1', dest / 'build.ps1')
code = (source / 'native/core.cpp').read_text(encoding='utf-8-sig')
protocol = 4 if args.effects else 3
message = '538' if args.effects else '539'
namespace = 'LeagueSkinEffectsProbe.v4.' if args.effects else 'LeagueSkinUpdateProbe.v3.'
code = code.replace('Protocol=2,', f'Protocol={protocol},')
code = re.sub(r'constexpr UINT BridgeMessage=WM_APP\+0x[0-9a-f]+;', f'constexpr UINT BridgeMessage=WM_APP+0x{message};', code)
code = code.replace('LeagueSkinCore.v2.', namespace).replace('LeagueSkinCore.Auto.v1.', namespace)
code = code.replace('#include "standalone_core.h"', '#include "standalone_core.h"\n#include "update_probe.h"')
assert 'static void process(Request* r) { independentProcess(r); }' in code
code = code.replace('static void process(Request* r) { independentProcess(r); }',
                    'static void process(Request* r) { updateProbe(r); }')
commands = '(r->command==6)' if args.effects else '(r->command==6 || r->command==10)'
code = re.sub(r'\(r->command>=6 && r->command<=\d+\)', commands, code)
(native / 'core.cpp').write_text(code, encoding='utf-8')
shutil.copy2(root / ('tools/skin-effects-probe.h' if args.effects else 'tools/skin-update-probe.h'), native / 'update_probe.h')
print(dest)
