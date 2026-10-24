"""Create a private recovery LP4 and a portable plugin package. No network writes."""
import hashlib
import json
import zipfile
from pathlib import Path
from prepare_profile import APP, ROOT


def archive(target, entries):
    target.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as z:
        for source, name in entries: z.write(source, name)
    with zipfile.ZipFile(target) as z:
        assert z.testzip() is None
        assert len(z.namelist()) == len(set(z.namelist())), 'Duplicate ZIP paths'
    return hashlib.sha256(target.read_bytes()).hexdigest()


def main():
    pid = json.loads((APP/'ApplicationInfo.json').read_text(encoding='utf-8-sig'))['defaultProfileName']
    profile = APP/'Profiles'/pid
    before = (profile/'ProfileInfo.json').read_bytes()
    files = {p.relative_to(profile).as_posix(): p for p in profile.rglob('*') if p.is_file()}
    for name in ['ApplicationInfo.json','ApplicationIcon.png']:
        if (APP/name).exists(): files[name] = APP/name
    entries = [(source,name) for name,source in sorted(files.items())]
    export = ROOT/'dist/Yamaha-installed.LP4'
    hashes = {export.name: archive(export, entries)}
    assert (profile/'ProfileInfo.json').read_bytes() == before, 'Profile changed during export; repeat export'
    package = ROOT/'dist/YamahaDirect'
    plugin = ROOT/'dist/YamahaDirect.lplug4'
    hashes[plugin.name] = archive(plugin, [(p,p.relative_to(package).as_posix()) for p in package.rglob('*') if p.is_file()])
    output = ROOT/'.local/SHA256SUMS.txt'
    output.write_text(''.join(f'{value}  {name}\n' for name,value in hashes.items()), encoding='utf-8')
    (ROOT/'dist/SHA256SUMS.txt').write_text(f'{hashes[plugin.name]}  {plugin.name}\n', encoding='utf-8')
    for name,value in hashes.items(): print(f'{name}: {value}')

if __name__ == '__main__': main()
