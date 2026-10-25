"""Stage only the eight existing Yamaha main-page bindings, preserving everything else."""
import copy
import hashlib
import json
from prepare_profile import APP, ROOT, ident, command, context_slot


def main():
    app = json.loads((APP/'ApplicationInfo.json').read_text(encoding='utf-8-sig'))
    path = APP/'Profiles'/app['defaultProfileName']/'ProfileInfo.json'
    original_bytes = path.read_bytes()
    original = json.loads(original_bytes.decode('utf-8-sig')); updated = copy.deepcopy(original)
    mode = next(m for m in updated['layout']['layoutModes'] if m['modeName'] == 'System')
    page = next(p for p in mode['touchPages'] if p['name'] == ident('touch'))
    assert len(page['controls']) == 12, 'Unexpected Yamaha page; inspect before migrating'
    for n in range(8):
        control = page['controls'][n+4]
        assert control['pressAction'] in (command('station'+str(n)), context_slot(n)), f'Yamaha slot {n+1} was customized; not overwriting it'
        control['pressAction'] = context_slot(n)
    check = copy.deepcopy(updated)
    check_page = next(p for m in check['layout']['layoutModes'] if m['modeName'] == 'System' for p in m['touchPages'] if p['name'] == ident('touch'))
    old_page = next(p for m in original['layout']['layoutModes'] if m['modeName'] == 'System' for p in m['touchPages'] if p['name'] == ident('touch'))
    for n in range(8): check_page['controls'][n+4]['pressAction'] = old_page['controls'][n+4]['pressAction']
    assert check == original, 'Unrelated profile content changed'
    stage = ROOT/'.tmp/context-slots'; stage.mkdir(parents=True, exist_ok=True)
    destination = stage/'ProfileInfo.json'
    destination.write_text(json.dumps(updated, ensure_ascii=False, indent=4)+'\n', encoding='utf-8')
    (stage/'plan.json').write_text(json.dumps({'profile_path':str(path),'staged_path':str(destination),'original_sha256':hashlib.sha256(original_bytes).hexdigest()},indent=2)+'\n',encoding='utf-8')
    print('PASS migration: only eight Yamaha press bindings changed; top row, dials, wheel, other workspaces and all Fn bindings preserved')

if __name__ == '__main__': main()
