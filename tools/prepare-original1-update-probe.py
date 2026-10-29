"""Prepare an isolated read-only probe using the existing Windows message bridge.

The diagnostic dispatch has no skin/camera write commands. Separate protocol,
message and mapping names avoid resident production DLLs consuming requests.
"""
from pathlib import Path
import shutil

root = Path(__file__).resolve().parents[1]
source = root / 'native/LeagueSkinCore'
dest = root / 'tmp/original1-update-probe'
native = dest / 'native'
native.mkdir(parents=True, exist_ok=True)
for path in (source / 'native').glob('*.h'):
    shutil.copy2(path, native / path.name)
shutil.copy2(source / 'build.ps1', dest / 'build.ps1')
code = (source / 'native/core.cpp').read_text(encoding='utf-8-sig')
code = code.replace('Protocol=2,', 'Protocol=3,').replace('WM_APP+0x533', 'WM_APP+0x539')
code = code.replace('LeagueSkinCore.v2.', 'LeagueSkinUpdateProbe.v3.')
code = code.replace('#include "standalone_core.h"', '#include "standalone_core.h"\n#include "update_probe.h"')
assert 'static void process(Request* r) { independentProcess(r); }' in code
code = code.replace('static void process(Request* r) { independentProcess(r); }',
                    'static void process(Request* r) { updateProbe(r); }')
code = code.replace('(r->command>=6 && r->command<=21)', '(r->command==6 || r->command==10)')
(native / 'core.cpp').write_text(code, encoding='utf-8')
shutil.copy2(root / 'tools/skin-update-probe.h', native / 'update_probe.h')
print(dest)
