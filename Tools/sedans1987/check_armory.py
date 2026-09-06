#!/usr/bin/env python3
"""Check showroom-to-Ledger wiring offline; --wire adds missing bridge references."""
import argparse
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--wire', action='store_true')
    args = parser.parse_args()
    expected = json.loads((HERE / 'lineup.json').read_text())
    expected += [row for row in json.loads((HERE / 'utilities.json').read_text())
                 if row['style'] in ('trail', 'ranger', 'highland', 'bastion')]
    expected.sort(key=lambda row: row['price'])
    catalog = (ROOT / 'Assets/Scripts/Outfit/ArmoryCatalog.cs').read_text()
    shelf = catalog.split('ArmoryItem[] Vehicles =', 1)[1].split('};', 1)[0]
    listings = re.findall(
        r'new ArmoryItem\(EquipmentKind.Vehicle, "([^"]+)", ([\d_]+),\s*'
        r'"([^"]+)", "([^"]+)"\)', shelf)
    assert len(listings) == len(expected) == 12, 'Expected eight sedans and four SUVs'
    for (name, price, note, model), row in zip(listings, expected):
        assert (name, int(price.replace('_', '')), model) == (
            row['name'], row['price'], row['id']), f'Catalogue drift: {name}'
        assert note.strip(), f'Missing description: {name}'

    bridge_path = ROOT / 'Assets/Configs/UI/Resources/LedgerModelSet.asset'
    bridge = bridge_path.read_text()
    before, rest = bridge.split('  vehicles:\n', 1)
    vehicles, after = rest.split('  people:\n', 1)
    scene = (ROOT / 'Assets/Scenes/Sedan1987Showroom.unity').read_text()
    for row in expected:
        prefab = ROOT / ('Assets/Sedan1987/Prefabs/' + row['id'] + '.prefab')
        guid = re.search(r'^guid: (\w+)$', Path(str(prefab) + '.meta').read_text(), re.M)[1]
        data = prefab.read_text()
        # Generated showroom bodies serialize their root GameObject first.
        root_id = re.search(r'^--- !u!1 &(\d+)$', data, re.M)[1]
        assert 'guid: ' + guid in scene, f'Not in showroom: {row["id"]}'
        reference = f'  - {{fileID: {root_id}, guid: {guid}, type: 3}}\n'
        if reference not in vehicles:
            assert args.wire, f'Missing Ledger reference: {row["id"]}; use --wire'
            vehicles += reference
    wired = before + '  vehicles:\n' + vehicles + '  people:\n' + after
    if wired != bridge:
        bridge_path.write_text(wired)
    print('PASS: 12 exact showroom listings, ascending prices and valid Ledger prefab references; no vans for sale')


if __name__ == '__main__':
    main()
