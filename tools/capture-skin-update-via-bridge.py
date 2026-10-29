"""Capture executable code through the isolated read-only Windows message probe."""
import argparse, hashlib, json, subprocess
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--host', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
host = args.host.resolve()
def call(*commands):
    result = subprocess.run([str(host), *commands], capture_output=True, text=True, encoding='utf8', timeout=12)
    reply = json.loads(result.stdout)
    if result.returncode or not reply.get('ok') or not reply.get('read_only') or reply.get('bridge_protocol') != 3 or not reply.get('transport_hook_released'):
        raise RuntimeError(f'probe_stopped:{reply.get("error", "invalid_reply")}')
    return reply

identity = call('status')
keys = ['pid','timestamp','image_size','code_rva','code_size']
data = bytearray()
for page in range((identity['code_size']+65535)//65536):
    reply = call('inventory', str(page))
    if any(reply.get(key) != identity[key] for key in keys) or reply.get('page') != page:
        raise RuntimeError('capture_identity_changed')
    chunk = bytes.fromhex(reply['hex'])
    if len(chunk) != min(65536, identity['code_size']-len(data)):
        raise RuntimeError('capture_incomplete_page')
    data.extend(chunk)
    if page % 64 == 0: print(f'Captured {len(data)}/{identity["code_size"]} executable bytes', flush=True)
final = call('status')
if any(final[key] != identity[key] for key in keys): raise RuntimeError('capture_identity_changed')
args.output.mkdir(parents=True, exist_ok=True)
(args.output/'game-code.bin').write_bytes(data)
image = bytearray(identity['image_size'])
image[identity['code_rva']:identity['code_rva']+len(data)] = data
(args.output/'runtime-code.bin').write_bytes(image)
manifest = {**{k:identity[k] for k in keys}, 'code_sha256':hashlib.sha256(data).hexdigest(),
    'complete':True, 'read_only':True, 'capture_method':'existing Windows message hook; isolated probe protocol 3; executable section only'}
(args.output/'capture.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf8')
print(json.dumps(manifest), flush=True)
