"""Read-only audit against the full CT snapshot from update_plugin.ps1."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
from prepare_profile import APP, ROOT, SERVICE, ident, command, context_slot


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--baseline', type=Path, required=True)
    backup = parser.parse_args().baseline.resolve()/'Applications'
    apps = SERVICE/'Applications/Loupedeck20'
    read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
    pid = read(APP/'ApplicationInfo.json')['defaultProfileName']
    profile_path = APP/'Profiles'/pid/'ProfileInfo.json'
    relative = profile_path.relative_to(apps)
    original = read(backup/relative)
    current = read(profile_path)
    normalized = copy.deepcopy(current)
    def page(profile):
        return next(p for m in profile['layout']['layoutModes'] if m['modeName'] == 'System' for p in m['touchPages'] if p['name'] == ident('touch'))
    for n in range(8):
        assert page(current)['controls'][n+4]['pressAction'] == context_slot(n), f'Context slot {n+1} missing'
        old = page(original)['controls'][n+4]['pressAction']
        assert old in (command('station'+str(n)), context_slot(n)), 'Unexpected baseline binding'
        page(normalized)['controls'][n+4]['pressAction'] = old
    assert normalized == original, 'An unrelated System profile setting changed'
    before = {p.relative_to(backup): p for p in backup.rglob('*') if p.is_file()}
    after = {p.relative_to(apps): p for p in apps.rglob('*') if p.is_file()}
    assert before.keys() == after.keys(), 'CT application-profile file list changed'
    for key in before.keys()-{relative}:
        assert digest(before[key]) == digest(after[key]), f'Other profile file changed: {key}'
    for name in ('YamahaDirectPlugin.dll', 'Yamaha.Core.dll'):
        assert digest(SERVICE/'Plugins/YamahaDirect/bin'/name) == digest(ROOT/'Yamaha.Loupedeck/bin/Release'/name), 'Installed binary mismatch: '+name
    report = {'context_slots': 8, 'unrelated_system_settings_identical': True, 'other_profile_files_identical': len(before)-1, 'installed_binaries_match': True}
    target = ROOT/'.local/context-verification.json'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
