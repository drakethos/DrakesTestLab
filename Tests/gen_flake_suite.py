"""Writes DrakesTestLab-Flake: a suite of deliberately flaky situations that only pass while the quiet zone works
(python gen_flake_suite.py).

Each F-test makes the kind of trouble that makes other tests flaky (a swarm of wild creatures, a wanderer, the game's raid
timer, a real raid) and checks it did no harm. The C-tests are controls: the same trouble with the quiet zone switched
off, which must get through. If the zone stops working the F-tests fail; if the trouble stops being real the C-tests fail,
so a green suite means the zone is doing the work and not that nothing happened."""
import json, os, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Assets', 'Presets', 'templates', 'suites', 'DrakesTestLab-Flake')
GUID = 'com.drakemods.testlab'

tests = {}


def T(tid, title, steps, asserts=None, desc='', **extra):
    t = {'name': f'{tid} {title}', 'description': desc, 'steps': steps,
         'asserts': (asserts or []) + [{'check': 'noErrors', 'source': 'DrakesTestLab'}]}
    t.update(extra)
    tests[tid] = t


def chk(kind, label=None, **f):
    s = {'do': 'check', 'check': kind, **f}
    if label:
        s['label'] = label
    return s


def wait(s=0.3):
    return {'do': 'wait', 'seconds': s}


def hook(name, **args):
    s = {'do': 'hook', 'name': name}
    if args:
        s['args'] = args
    return s


def wild(dist, prefab='Boar'):
    return hook('quiet.spawnWild', prefab=prefab, dist=dist)


def count(prefab='', radius=40):
    return hook('quiet.creaturesNear', prefab=prefab, radius=radius)


QUIET_OFF = {'do': 'setConfig', 'guid': GUID, 'values': {'Arena/QuietZone': 'Off'}}
EVENT = 'army_eikthyr'

suite = {
    'name': 'DrakesTestLab-Flake',
    'description': "Flaky situations (wild swarms, wanderers, raids) that must do no harm inside the quiet zone, and controls that prove the trouble is real with the zone off.",
    'requireMods': [{'guid': GUID, 'minVersion': '0.5.0'}],
    'include': '_lib/valheim.json',
    'setup': {'timeOfDay': 0.5, 'freezeTime': 1, 'god': 1, 'noBuild': 'off', 'hideUnlockPopups': 1},
    'resetBetweenTests': True,
}

# ------------------------------------------------------------ the zone must hold
T('F-01', 'a swarm of six wild creatures around the player is gone', [
    hook('quiet.mark'),
    wild(3), wild(5), wild(7), wild(9), wild(11), wild(13),
    wait(2.0),
    count('', 40),
    chk('lastResult', 'not one creature is left within 40 m', equals=0),
    hook('quiet.removedSince'),
    chk('lastResult', 'the zone removed all six', min=6),
], desc='A burst of wild creatures at once: the sweep has to catch every one, not just the first.')

T('F-02', 'wild creatures go, the creature the test made stays', [
    {'do': 'spawn', 'prefab': 'Boar', 'dz': 6, 'as': 'mine'},
    wild(4), wild(8), wild(12),
    wait(2.0),
    count('Boar', 40),
    chk('lastResult', 'exactly one boar left: the one the test spawned', equals=1),
    chk('objectNear', 'and it is the test boar', target='Boar', radius=10),
], desc='Selectivity: the zone must never touch what the test (or a mod) made itself.')

T('F-03', 'a wanderer outside the zone is left alone, then removed when the zone reaches it', [
    {'do': 'setConfig', 'guid': GUID, 'values': {'Arena/QuietZoneRadius': '30'}},
    wild(60),
    wait(1.0),
    count('Boar', 200),
    chk('lastResult', 'outside the zone (30 m) it is left alone', equals=1),
    {'do': 'teleport', 'dx': 0, 'dz': 40},
    wait(2.0),
    count('Boar', 200),
    chk('lastResult', 'the zone moved with the player and removed it', equals=0),
], desc='The zone follows the player: a creature that was outside is removed once the player is within the radius of it.')

T('F-04', "the game's raid timer is held", [
    hook('quiet.mark'),
    hook('quiet.timerEvent'), hook('quiet.timerEvent'), hook('quiet.timerEvent'),
    hook('quiet.blockedEventsSince'),
    chk('lastResult', 'the timer tried three times and was refused every time', equals=3),
    hook('quiet.currentEvent'),
    chk('lastResult', 'no event is running', equals='none'),
], desc='The same call the game makes when its random-event timer fires.')

T('F-05', 'a real raid starts but nothing spawns inside the zone', [
    hook('quiet.mark'),
    {'do': 'console', 'command': f'event {EVENT}'},
    hook('quiet.currentEvent'),
    chk('lastResult', 'a raid a test starts on purpose still runs', equals=EVENT),
    wait(25),
    hook('quiet.blockedSpawnsSince'),
    chk('lastResult', 'the raid tried to spawn and was refused', min=1),
    count('', 80),
    chk('lastResult', 'no creature came out of it', equals=0),
], timeout=90, desc="A real raid (Eikthyr's army) for 25 s. Slow on purpose.")

T('F-06', 'the previous raid did not carry over into this test', [
    hook('quiet.currentEvent'),
    chk('lastResult', 'no event running at the start of the next test', equals='none'),
], desc='Needs F-05 before it (the reset between tests ends a running event).')

T('F-07', 'the zone catches what was alive when it switched on', [
    QUIET_OFF,
    {'do': 'spawn', 'prefab': 'Boar', 'dz': 6},
    wait(1.0),
    count('Boar', 40),
    chk('lastResult', 'with the zone off the boar is alive', min=1),
    {'do': 'setConfig', 'guid': GUID, 'values': {'Arena/QuietZone': 'Tests'}},
    wait(2.0),
    count('Boar', 40),
    chk('lastResult', 'switched on, it counts what is already there as wild and removes it', equals=0),
], desc='Documents the edge: creatures alive when the zone comes on are treated as wild.')

# ------------------------------------------------------------ a clean landscape: clear areas, add what a test needs
def natural(prefab, dz):
    return {'do': 'spawn', 'prefab': prefab, 'dz': dz}


def near(category, expect=True, radius=30, label=None):
    return chk('objectNear', label, category=category, radius=radius, expect=expect)


NATURALS = ['trees', 'stumps', 'logs', 'bushes', 'pickables']

T('F-08', 'clearArea removes every kind of natural object and says how many', [
    natural('Beech1', 4), natural('Beech_Stub', 6), natural('Beech_log', 8), natural('Bush01', 10), natural('Pickable_Mushroom', 12),
    near('trees', label='a tree is there'), near('stumps', label='a stump'), near('logs', label='a log'),
    near('bushes', label='a bush'), near('pickables', label='a plant to pick'),
    {'do': 'clearArea', 'radius': 30, 'what': NATURALS},
    chk('lastResult', 'it reported what it removed', min=5),
    near('trees', False, label='no tree'), near('stumps', False, label='no stump'), near('logs', False, label='no log'),
    near('bushes', False, label='no bush'), near('pickables', False, label='no plant'),
], desc='Needs the prefabs Beech1, Beech_Stub, Beech_log, Bush01 and Pickable_Mushroom.')

T('F-09', 'clearArea only removes the kinds it is asked to', [
    natural('Beech1', 4), natural('Beech_log', 7),
    {'do': 'clearArea', 'radius': 30, 'what': ['logs']},
    near('logs', False, label='the log is gone'),
    near('trees', True, label='the tree is still there'),
])

T('F-10', 'leave trees and a stump standing for the next test', [
    {'do': 'spawn', 'prefab': 'Beech1', 'dz': 6, 'keep': True},
    {'do': 'spawn', 'prefab': 'Beech_Stub', 'dz': 8, 'keep': True},
    near('trees', label='they are standing now'),
], desc='Setup for F-11 (they stay after the test ends).')

T('F-11', 'a test with clearArea starts with the area clear', [
    near('landscape', False, label='the trees F-10 left behind were cleared before the first step'),
], clearArea={'radius': 30, 'what': ['landscape']}, desc='Needs F-10 before it. The test field clearArea runs after the reset and the setup, before the steps.')

T('F-12', 'the arena is clear far out, so the one tree a test adds is the only one', [
    {'do': 'spawn', 'prefab': 'Beech1', 'dz': 8, 'as': 'tree'},
    {'do': 'teleport', 'to': 'tree', 'distance': 2},
    chk('objectNear', 'the tree it added is within reach', target='tree', radius=4),
    near('trees', True, radius=60, label='there is a tree within 60 m'),
    chk('objectNear', 'and it is the only one: no natural tree within 60 m', category='trees', radius=60, count=1),
], desc='The lab clears trees, stumps, logs, bushes, plants and rocks within [Arena] ClearRadius (70 m), so a test that needs a tree adds it.')

# ------------------------------------------------------------ controls: the trouble is real with the zone off
T('C-01', 'control: with the zone off the same swarm stays', [
    QUIET_OFF,
    hook('quiet.mark'),
    wild(3), wild(5), wild(7), wild(9), wild(11), wild(13),
    wait(2.0),
    count('', 40),
    chk('lastResult', 'all six are still around the player', min=6),
    hook('quiet.removedSince'),
    chk('lastResult', 'and the zone removed nothing', equals=0),
    hook('quiet.clearCreatures', radius=40),
], desc='If this fails the flaky situation is no longer real, so a green F-01 would prove nothing.')

T('C-02', "control: with the zone off the game's raid timer is not held", [
    QUIET_OFF,
    hook('quiet.mark'),
    hook('quiet.timerEvent'), hook('quiet.timerEvent'), hook('quiet.timerEvent'),
    hook('quiet.blockedEventsSince'),
    chk('lastResult', 'nothing was refused', equals=0),
])

T('C-03', 'control: with the zone off a raid is not refused its spawns', [
    QUIET_OFF,
    hook('quiet.mark'),
    {'do': 'console', 'command': f'event {EVENT}'},
    wait(25),
    hook('quiet.blockedSpawnsSince'),
    chk('lastResult', 'no spawn was refused', equals=0),
    hook('quiet.clearCreatures', radius=80),
], timeout=90, desc='Slow on purpose. Proves the raid spawner runs when the zone is off.')

# ---------------------------------------------------------------- write
if os.path.isdir(OUT):
    shutil.rmtree(OUT)
os.makedirs(OUT)
with open(os.path.join(OUT, 'suite.json'), 'w', encoding='utf-8') as f:
    json.dump(suite, f, indent=2, ensure_ascii=False)
for tid, t in tests.items():
    with open(os.path.join(OUT, f'{tid}.json'), 'w', encoding='utf-8') as f:
        json.dump(t, f, indent=2, ensure_ascii=False)
print(f'{len(tests)} flake tests')
