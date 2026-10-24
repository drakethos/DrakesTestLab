"""Writes DrakesTestLab's own meta suite: it tests the tester (python gen_meta_suite.py).

DrakesTestLab-Meta exercises every step and check against plain Valheim (no other mod needed), plus negative tests
("expectFailure") that prove a broken test is reported as failed: a failing check, a step that cannot be done, a timeout,
an unknown hook. The suite ships with the mod (Assets/Presets/templates/suites), so it doubles as a worked example."""
import json, os, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Assets', 'Presets', 'templates', 'suites', 'DrakesTestLab-Meta')
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


suite = {
    'name': 'DrakesTestLab-Meta',
    'description': "Tests the tester: every step and check against plain Valheim, and negative tests proving broken tests are caught.",
    'requireMods': [{'guid': GUID, 'minVersion': '0.2.0'}],
    'configOverrides': [{'guid': GUID, 'section': 'Rules', 'key': 'FallDamageMultiplier', 'value': '0.25'}],
    'include': '_lib/valheim.json',
    'setup': {'timeOfDay': 0.5, 'freezeTime': 1, 'god': 1, 'noBuild': 'off', 'hideUnlockPopups': 1},
    'resetBetweenTests': True,
}

# ---------------------------------------------------------------- inventory
T('M-01', 'give, hasItem, noItem, equipped, clearInventory', [
    {'do': 'give', 'prefab': 'Wood', 'count': 25},
    {'do': 'give', 'prefab': 'SwordBronze', 'quality': 3},
    {'do': 'give', 'prefab': 'ShieldWood', 'equip': True},
    chk('hasItem', 'at least 25 wood', prefab='Wood', count=25),
    chk('hasItem', 'the sword at quality 3', prefab='SwordBronze', quality=3),
    chk('noItem', 'no stone in the bag', prefab='Stone'),
    chk('equipped', 'the shield is worn', prefab='ShieldWood'),
    chk('hasItem', 'given items are unowned', prefab='Wood', crafter='none'),
    {'do': 'clearInventory'},
    chk('noItem', 'clearInventory empties the bag', prefab='Wood'),
    chk('noItem', 'and takes off worn items', prefab='ShieldWood'),
])

T('M-02', 'craft (free and by someone else) sets the real crafter', [
    {'do': 'craft', 'prefab': 'Hammer'},
    chk('hasItem', 'crafted', prefab='Hammer'),
    chk('hasItem', 'crafted by me', prefab='Hammer', crafter='me'),
    {'do': 'craft', 'prefab': 'Torch', 'by': 'Alice'},
    chk('hasItem', 'crafted by Alice', prefab='Torch', crafter='Alice'),
    chk('noItem', 'nothing else appeared', prefab='Club'),
])

T('M-03', 'real-cost craft at a placed workbench (materialsFor, station)', [
    {'do': 'give', 'materialsFor': 'AxeFlint'},
    {'do': 'place', 'prefab': 'piece_workbench', 'dz': 3, 'dx': 2, 'as': 'bench'},
    {'do': 'craft', 'prefab': 'AxeFlint', 'free': False, 'station': 'bench'},
    chk('hasItem', 'the flint axe was made', prefab='AxeFlint', crafter='me'),
    chk('noItem', 'the flint was used up', prefab='Flint'),
])

T('M-04', 'the shared function library (include, functions, string calls)', [
    {'do': 'craftAndCheck', 'prefab': 'Hammer'},
    'equipAndCheck("Club")',
    {'do': 'spawnPiece', 'prefab': 'piece_chest_wood', 'owner': 'Alice', 'tag': 'chest'},
    {'do': 'as', 'who': 'Bob', 'steps': [{'do': 'log', 'message': 'a step run as Bob'}]},
], [
    {'check': 'owner', 'target': 'chest', 'expect': 'Alice'},
    {'check': 'logContains', 'text': 'Now playing as Bob', 'source': 'DrakesTestLab'},
])

# ---------------------------------------------------------------- building, objects, containers
T('M-05', 'place (hammer path), owner, objectNear, interact, store, containerHas', [
    {'do': 'place', 'prefab': 'piece_chest_wood', 'dz': 2.5, 'as': 'chest'},
    chk('owner', 'I built it', target='chest', expect='me'),
    chk('objectNear', 'it is there', target='piece_chest_wood', radius=5),
    {'do': 'give', 'prefab': 'Stone', 'count': 30},
    {'do': 'store', 'target': 'chest', 'prefab': 'Stone', 'count': 20},
    chk('containerHas', 'the chest holds 20 stone', target='chest', prefab='Stone', count=20),
    chk('hasItem', '10 stayed with me', prefab='Stone', count=10),
    chk('containerHas', 'no wood in the chest', target='chest', prefab='Wood', expect=False),
    {'do': 'interact', 'target': 'chest'}, wait(0.4),
    chk('uiVisible', 'E opens the chest', name='InventoryGui'),
    {'do': 'closeUi'}, wait(0.2),
    chk('uiVisible', 'closeUi closes it', name='InventoryGui', expect=False),
])

T('M-06', 'place refusals: no materials, unknown piece', [
    {'do': 'place', 'prefab': 'piece_workbench', 'free': False, 'expect': False, 'label': 'no wood: the hammer refuses'},
    {'do': 'give', 'materialsFor': 'piece_workbench'},
    {'do': 'place', 'prefab': 'piece_workbench', 'free': False, 'as': 'bench', 'label': 'with the wood it builds'},
    chk('noItem', 'and the wood is used', prefab='Wood'),
])

T('M-07', 'spawn, drop, destroy', [
    {'do': 'spawn', 'prefab': 'Boar', 'dz': 6, 'as': 'boar'},
    chk('objectNear', 'a boar appeared', target='Boar', radius=10),
    {'do': 'give', 'prefab': 'Resin', 'count': 3},
    {'do': 'drop', 'prefab': 'Resin', 'as': 'resin'},
    wait(1.0),
    chk('noItem', 'dropped from the bag', prefab='Resin'),
    chk('objectNear', 'lying on the ground, not picked up again', target='Resin', radius=4),
    {'do': 'destroy', 'target': 'boar'}, wait(0.3),
    chk('objectNear', 'destroy removed the boar', target='Boar', radius=10, expect=False),
])

T('M-08', 'zdo, hoverText, lookAt, hovering, crosshair', [
    {'do': 'place', 'prefab': 'piece_workbench', 'dz': 3, 'as': 'bench'},
    {'do': 'lookAt', 'target': 'bench', 'distance': 1.5},
    chk('hovering', 'the crosshair is on the workbench', target='bench'),
    {'do': 'wait', 'until': {'check': 'crosshair', 'contains': 'Workbench'}, 'timeout': 3, 'label': 'the hover panel fades in: wait for the name at the crosshair'},
    chk('crosshair', 'its name at the crosshair', contains='Workbench'),
    chk('hoverText', 'its hover text', target='bench', contains='Workbench'),
    chk('zdo', 'a never-saved key reads its default', target='bench', key='drake_meta_none', expect='x', default='x'),
])

# ---------------------------------------------------------------- movement and position
T('M-09', 'teleport, face, position', [
    {'do': 'teleport', 'dx': 0, 'dz': 15},
    wait(0.2),
    {'do': 'spawn', 'prefab': 'piece_chest_wood', 'dz': 4, 'as': 'chest'},
    {'do': 'teleport', 'to': 'chest', 'distance': 2},
    chk('position', 'next to the chest', target='chest', radius=3),
    {'do': 'face', 'target': 'chest'},
])

# ---------------------------------------------------------------- identity and wards
T('M-10', 'playAs, wards, wardAccess, owner', [
    {'do': 'spawn', 'prefab': 'guard_stone', 'dz': 2, 'owner': 'Amy', 'enabled': True, 'radius': 12, 'permitted': ['Carol'], 'as': 'ward'},
    {'do': 'playAs', 'who': 'Bob'},
    chk('wardAccess', 'Bob is a stranger', expect=False),
    {'do': 'playAs', 'who': 'Carol'},
    chk('wardAccess', 'Carol is on the permitted list', expect=True),
    {'do': 'playAs', 'who': 'Amy'},
    chk('wardAccess', 'Amy owns it', expect=True),
    {'do': 'playAs', 'who': 'me'},
], [{'check': 'owner', 'target': 'ward', 'expect': 'Amy'}])

T('M-11', 'reset between tests puts identity back', [
    chk('logContains', 'observed: the reset ran before this test', text='Test state reset', source='DrakesTestLab'),
    {'do': 'spawn', 'prefab': 'guard_stone', 'dz': 2, 'owner': 'me', 'enabled': True, 'radius': 12},
    chk('wardAccess', 'I am myself again (not Amy from the last test)', expect=True),
])

# ---------------------------------------------------------------- config
T('M-12', 'suite configOverrides and test setConfig', [
    chk('config', 'the suite override is on', guid=GUID, section='Rules', key='FallDamageMultiplier', expect='0.25'),
    {'do': 'setConfig', 'values': {'Rules/FallDamageMultiplier': '0.75'}},
    chk('config', 'setConfig changed it for this test', guid=GUID, section='Rules', key='FallDamageMultiplier', expect='0.75'),
])

T('M-13', 'setConfig is put back after the test', [
    chk('config', 'back to the suite value', guid=GUID, section='Rules', key='FallDamageMultiplier', expect='0.25'),
])

# ---------------------------------------------------------------- reset between tests
T('M-14', 'reset between tests: leave things behind', [
    {'do': 'give', 'prefab': 'Coins', 'count': 77},
    {'do': 'console', 'command': 'setkey drake_meta_key'},
    chk('globalKey', 'console set a world key', key='drake_meta_key'),
])

T('M-15', 'reset between tests: the next test starts clean', [
    chk('noItem', 'the coins are gone', prefab='Coins'),
    wait(0.1),
])

# ---------------------------------------------------------------- setup (case format) and cases
T('M-16', 'test setup: items, skills, vitals, keys', [
    chk('hasItem', 'setup items', prefab='Bronze', count=5),
    chk('equipped', 'setup equip', prefab='HelmetBronze'),
    chk('skill', 'setup skills', name='Swords', min=40),
    chk('stat', 'carry weight from setup', name='carryWeight', min=999),
    chk('globalKey', 'setup world key', key='defeated_eikthyr'),
], setup={'items': [{'prefab': 'Bronze', 'count': 5}, {'prefab': 'HelmetBronze', 'equip': True}],
          'skills': [{'name': 'Swords', 'level': 40}], 'carryWeight': 1000, 'globalKeys': ['defeated_eikthyr']})

T('M-17', 'bosses and guardian power', [
    chk('guardianPower', 'Eikthyr made my guardian power', contains='Eikthyr'),
    chk('globalKey', 'and set the boss key', key='defeated_eikthyr'),
], setup={'bosses': ['Eikthyr']})

T('M-18', 'case step: apply a shipped case', [
    {'do': 'case', 'name': 'builder-sandbox'},
    chk('globalKey', 'everything unlocked', key='AllPiecesUnlocked'),
    chk('stat', 'huge carry weight', name='carryWeight', min=1000),
])

# ---------------------------------------------------------------- hooks, invoke, lastResult
T('M-19', 'hook and lastResult compares', [
    {'do': 'hook', 'name': 'testlab.echo', 'args': {'value': 'hello world'}, 'expect': 'hello world'},
    {'do': 'hook', 'name': 'testlab.echo', 'args': {'value': 'hello world'}, 'contains': 'world'},
    {'do': 'hook', 'name': 'testlab.echo', 'args': {'value': 'hello world'}, 'notContains': 'bye'},
    {'do': 'give', 'prefab': 'Wood', 'count': 3},
    {'do': 'hook', 'name': 'testlab.inventoryCount'},
    chk('lastResult', 'the last hook answer is kept', min=1),
], [
    {'check': 'hook', 'name': 'testlab.echo', 'args': {'value': 42}, 'expect': '42'},
    {'check': 'hook', 'name': 'testlab.echo', 'args': {'value': True}},
])

# ---------------------------------------------------------------- input
T('M-20', 'holdKey, releaseKeys, pressKey', [
    {'do': 'holdKey', 'key': 'LeftShift'},
    {'do': 'hook', 'name': 'testlab.keyHeld', 'args': {'key': 'LeftShift'}, 'expect': True, 'label': 'the game sees Shift held'},
    {'do': 'releaseKeys'},
    {'do': 'hook', 'name': 'testlab.keyHeld', 'args': {'key': 'LeftShift'}, 'expect': False, 'label': 'released'},
    {'do': 'pressKey', 'button': 'Inventory'}, wait(0.5),
    chk('uiVisible', 'the Inventory key opened the inventory', name='InventoryGui'),
    {'do': 'closeUi'},
])

T('M-21', 'held keys are let go when a test ends', [
    {'do': 'hook', 'name': 'testlab.keyHeld', 'args': {'key': 'LeftShift'}, 'expect': False, 'label': 'Shift is not stuck from an earlier test'},
    {'do': 'holdKey', 'key': 'LeftShift'},
])

T('M-22', '...checked again after a test that held a key to the end', [
    {'do': 'hook', 'name': 'testlab.keyHeld', 'args': {'key': 'LeftShift'}, 'expect': False, 'label': 'Shift was released by the runner'},
])

T('M-23', 'itemClick opens the inventory and clicks a slot', [
    {'do': 'give', 'prefab': 'Club'},
    {'do': 'itemClick', 'prefab': 'Club', 'button': 'right'}, wait(0.4),
    chk('equipped', 'right-clicking the club wields it, like a player', prefab='Club'),
    {'do': 'closeUi'},
])

T('M-24', 'type into a sign (TextInput), zdo', [
    {'do': 'place', 'prefab': 'sign', 'dz': 2.5, 'as': 'sign'},
    {'do': 'interact', 'target': 'sign'}, wait(0.4),
    chk('uiVisible', 'the sign asks for text', name='TextInput'),
    {'do': 'type', 'field': '*', 'text': 'Meta was here', 'submit': True}, wait(0.4),
    chk('uiVisible', 'submit closed the prompt', name='TextInput', expect=False),
    chk('zdo', 'the sign saved the text', target='sign', key='text', contains='Meta was here'),
    chk('hoverText', 'and shows it', target='sign', contains='Meta was here'),
    {'do': 'closeUi'},
])

# ---------------------------------------------------------------- language
T('M-25', 'setLanguage switches for one test', [
    {'do': 'setLanguage', 'language': 'Spanish'},
    {'do': 'place', 'prefab': 'piece_workbench', 'dz': 3, 'as': 'bench'},
    {'do': 'lookAt', 'target': 'bench', 'distance': 1.5},
    chk('hoverText', 'the workbench reads in Spanish', target='bench', contains='Banco'),
])

T('M-26', 'the language is back to English afterwards', [
    {'do': 'place', 'prefab': 'piece_workbench', 'dz': 3, 'as': 'bench'},
    {'do': 'lookAt', 'target': 'bench', 'distance': 1.5},
    chk('hoverText', 'English again', target='bench', contains='Workbench'),
])

# ---------------------------------------------------------------- messages, log, report
T('M-27', 'message and unlockShown capture, logContains, log, screenshot', [
    {'do': 'craft', 'prefab': 'Hammer'},
    wait(0.1),
    {'do': 'spawn', 'prefab': 'guard_stone', 'dz': 2, 'owner': 'Amy', 'enabled': True, 'radius': 12},
    {'do': 'spawn', 'prefab': 'piece_chest_wood', 'dz': 3, 'owner': 'Amy', 'as': 'chest'},
    {'do': 'playAs', 'who': 'Bob'},
    {'do': 'interact', 'target': 'chest'}, wait(0.5),
    chk('uiVisible', 'no chest window for Bob', name='InventoryGui', expect=False),
    {'do': 'playAs', 'who': 'me'},
    {'do': 'log', 'message': 'meta log line'},
    {'do': 'screenshot', 'name': 'meta-proof'},
    chk('logContains', 'the run logs who I play as', text='Now playing as Bob', source='DrakesTestLab'),
])

# ---------------------------------------------------------------- persistence
T('M-28', 'relog: built pieces and chest contents survive; tags are found again', [
    {'do': 'place', 'prefab': 'piece_chest_wood', 'dz': 2.5, 'as': 'chest', 'keep': True},
    {'do': 'give', 'prefab': 'Flint', 'count': 7},
    {'do': 'store', 'target': 'chest', 'prefab': 'Flint'},
    {'do': 'relog', 'timeout': 240},
    chk('containerHas', 'the flint is still in the chest after logging out and in', target='chest', prefab='Flint', count=7),
    chk('owner', 'still mine', target='chest', expect='me'),
    {'do': 'destroy', 'target': 'chest'},
], timeout=400)

# ---------------------------------------------------------------- wait
T('M-29', 'wait until (a check that becomes true)', [
    {'do': 'spawn', 'prefab': 'Boar', 'dz': 6},
    {'do': 'wait', 'until': {'check': 'objectNear', 'target': 'Boar', 'radius': 12}, 'timeout': 5},
])

# ---------------------------------------------------------------- 0.3: save as case, recorder, toolbox
T('M-30', 'save the current game as a case, reset, apply it again', [
    {'do': 'craft', 'prefab': 'Hammer'},
    {'do': 'give', 'prefab': 'Wood', 'count': 33},
    {'do': 'place', 'prefab': 'piece_chest_wood', 'dz': 3, 'as': 'box'},
    {'do': 'store', 'target': 'box', 'prefab': 'Wood', 'count': 10},
    {'do': 'spawn', 'prefab': 'guard_stone', 'dz': 2, 'dx': -3, 'owner': 'Amy', 'enabled': True, 'radius': 10, 'permitted': ['Carol']},
    {'do': 'hook', 'name': 'testlab.saveCase', 'args': {'name': 'meta-roundtrip', 'radius': 12}, 'contains': 'meta-roundtrip', 'label': 'saved the case'},
    {'do': 'hook', 'name': 'testlab.caseText', 'args': {'name': 'meta-roundtrip'}, 'contains': '"prefab": "piece_chest_wood"', 'label': 'the chest is in the saved build'},
    {'do': 'hook', 'name': 'testlab.caseText', 'args': {'name': 'meta-roundtrip'}, 'contains': '"crafter": "me"', 'label': 'the crafted hammer keeps its crafter'},
    {'do': 'hook', 'name': 'testlab.caseText', 'args': {'name': 'meta-roundtrip'}, 'contains': '"owner": "Amy"', 'label': 'the ward keeps its owner'},
    {'do': 'destroy', 'target': 'box'},
    {'do': 'reset'},
    chk('noItem', 'after the reset the bag is empty', prefab='Wood'),
    {'do': 'case', 'name': 'meta-roundtrip'},
    wait(1.0),
    chk('hasItem', 'the wood is back', prefab='Wood', count=23),
    chk('hasItem', 'the hammer, still made by me', prefab='Hammer', crafter='me'),
    chk('containerHas', 'the chest is rebuilt with its 10 wood', target='piece_chest_wood', prefab='Wood', count=10),
    chk('owner', 'Amy owns the ward again', target='guard_stone', expect='Amy'),
], desc='Save as case writes the inventory (with crafters), the build around you (chests with contents) and wards; applying it brings them back.')

T('M-31', 'the recorder turns actions into steps', [
    {'do': 'hook', 'name': 'testlab.recordStart', 'args': {'setup': False}, 'expect': True},
    {'do': 'craft', 'prefab': 'Hammer'},
    {'do': 'give', 'prefab': 'Stone', 'count': 5},
    {'do': 'place', 'prefab': 'piece_chest_wood', 'dz': 3, 'as': 'box'},
    {'do': 'playAs', 'who': 'Bob'},
    {'do': 'playAs', 'who': 'me'},
    {'do': 'console', 'command': 'setkey drake_meta_recorded'},
    {'do': 'hook', 'name': 'testlab.recordStop', 'label': 'stop'},
    chk('lastResult', 'at least six steps were recorded', min=6),
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=craft, prefab=Hammer', 'label': 'the craft'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=give, prefab=Stone, count=5', 'label': 'the give'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=place, prefab=piece_chest_wood', 'label': 'the place'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'as=chest1', 'label': 'the new chest got a tag'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=playAs, who=Bob', 'label': 'play as Bob'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=console, command=setkey drake_meta_recorded', 'label': 'the console command'},
    {'do': 'hook', 'name': 'testlab.recordSave', 'args': {'suite': '_recorded-meta', 'name': 'recorded'}, 'contains': 'recorded.json', 'label': 'saved as a test'},
])

T('M-33', 'record a player-style flow and save it as a test (replayed by the Recorded-Replay suite)', [
    {'do': 'place', 'prefab': 'piece_workbench', 'dz': 3, 'dx': -3, 'as': 'bench', 'label': 'a workbench, so building at real cost works (part of the recorded setup)'},
    {'do': 'give', 'materialsFor': 'piece_chest_wood'},
    {'do': 'give', 'materialsFor': 'sign'},
    {'do': 'give', 'prefab': 'Wood', 'count': 20},
    {'do': 'give', 'prefab': 'Club'},
    {'do': 'hook', 'name': 'testlab.recordStart', 'args': {'setup': True}, 'expect': True, 'label': 'record, starting from the game as it is now'},
    wait(0.3),
    {'do': 'screenshot', 'name': 'lab-bar-recording'},
    {'do': 'place', 'prefab': 'piece_chest_wood', 'dz': 2.5, 'free': False, 'as': 'box'},
    {'do': 'interact', 'target': 'box', 'asPlayer': True}, wait(0.4),
    {'do': 'store', 'target': 'box', 'prefab': 'Wood', 'count': 8},
    {'do': 'closeUi'}, wait(0.3),
    {'do': 'place', 'prefab': 'sign', 'dz': 2.5, 'dx': 2, 'free': False, 'as': 'board'},
    {'do': 'interact', 'target': 'board', 'asPlayer': True}, wait(0.4),
    {'do': 'type', 'field': '*', 'text': 'Recorded!', 'submit': True}, wait(0.3),
    {'do': 'itemClick', 'prefab': 'Club', 'button': 'right'}, wait(0.3),
    {'do': 'closeUi'},
    {'do': 'hook', 'name': 'testlab.recordCheckInventory', 'label': 'add inventory checks'},
    {'do': 'hook', 'name': 'testlab.recordStop'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=interact, target=chest1', 'label': 'pressing E on the new chest'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=store, target=chest1, prefab=Wood, count=8', 'label': 'moving wood into it'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=type, field=*, text=Recorded!, submit=true', 'label': 'typing on the sign'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=itemClick, prefab=Club', 'label': 'right-clicking the club'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=check, check=hasItem', 'label': 'the inventory checks'},
    {'do': 'hook', 'name': 'testlab.recordedSteps', 'contains': 'do=closeUi', 'label': 'closing the chest window'},
    {'do': 'hook', 'name': 'testlab.recordSave', 'args': {'suite': 'Recorded-Replay', 'name': 'flow'}, 'contains': 'flow.json', 'label': 'saved into the Recorded-Replay suite'},
], desc='The second half of the proof is running the saved Recorded-Replay suite: the recorded test must pass on its own.')

T('M-32', 'the toolbox: new layout (screenshots)', [
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Cases'}}, wait(0.6),
    {'do': 'screenshot', 'name': 'toolbox-cases'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Record'}}, wait(0.4),
    {'do': 'screenshot', 'name': 'toolbox-record'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Player'}}, wait(0.4),
    {'do': 'screenshot', 'name': 'toolbox-player'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Rules'}}, wait(0.4),
    {'do': 'screenshot', 'name': 'toolbox-rules'},
    {'do': 'hook', 'name': 'testlab.recordStart', 'args': {'setup': False}},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Record'}}, wait(0.4),
    {'do': 'screenshot', 'name': 'bar-with-lab-open'},
    {'do': 'hook', 'name': 'testlab.recordStop'},
    {'do': 'hook', 'name': 'testlab.closeToolbox'},
], desc='Proof of the 0.3 toolbox layout: sidebar groups, cards. The images are in the report.')

T('M-34', 'UI tour: every tab, opacity, the lab bar (screenshots for the docs)', [
    {'do': 'setConfig', 'guid': 'com.drakemods.testlab', 'values': {'Dev/UiOpacity': '1'}},
    {'do': 'give', 'prefab': 'Hammer'}, {'do': 'give', 'prefab': 'SwordBronze'}, {'do': 'give', 'prefab': 'Wood', 'count': 30},
    {'do': 'place', 'prefab': 'piece_chest_wood', 'dz': 3, 'as': 'box'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'World'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-world'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Player'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-player'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Spawn'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-spawn'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Rules'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-rules'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Cases'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-cases'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Wards'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-wards'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Tests'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-tests'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Record'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-record'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Inspect'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-inspect'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Mods'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-mods'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Log'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-log'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Session'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'tab-session'},
    {'do': 'setConfig', 'guid': 'com.drakemods.testlab', 'values': {'Dev/UiOpacity': '0.6'}},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'World'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'opacity-60'},
    {'do': 'setConfig', 'guid': 'com.drakemods.testlab', 'values': {'Dev/UiOpacity': '1'}},
    {'do': 'hook', 'name': 'testlab.closeToolbox'},
    {'do': 'hook', 'name': 'testlab.recordStart', 'args': {'setup': False}}, wait(0.4),
    {'do': 'lookAt', 'target': 'box', 'distance': 2},
    {'do': 'screenshot', 'name': 'lab-bar-recording'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Record'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'lab-bar-with-window'},
    {'do': 'hook', 'name': 'testlab.recordStop'},
    {'do': 'hook', 'name': 'testlab.closeToolbox'},
], desc='Pictures of the whole lab for the README and the wiki. setConfig puts the opacity back after the test.')

T('M-35', 'feature showcase: a real recording, a saved case, Player 2, the manual-check bar (screenshots)', [
    {'do': 'setConfig', 'guid': 'com.drakemods.testlab', 'values': {'Dev/UiOpacity': '1'}},
    {'do': 'place', 'prefab': 'piece_workbench', 'dz': 3, 'dx': -3, 'as': 'bench'},
    {'do': 'give', 'materialsFor': 'piece_chest_wood'}, {'do': 'give', 'materialsFor': 'sign'},
    {'do': 'give', 'prefab': 'Wood', 'count': 20}, {'do': 'give', 'prefab': 'SwordBronze', 'quality': 2},
    # a real recording, shown in the Record tab
    {'do': 'hook', 'name': 'testlab.recordStart', 'args': {'setup': True}},
    {'do': 'place', 'prefab': 'piece_chest_wood', 'dz': 2.5, 'free': False, 'as': 'box'},
    {'do': 'interact', 'target': 'box', 'asPlayer': True}, wait(0.4),
    {'do': 'store', 'target': 'box', 'prefab': 'Wood', 'count': 8},
    {'do': 'closeUi'}, wait(0.3),
    {'do': 'place', 'prefab': 'sign', 'dz': 2.5, 'dx': 2, 'free': False, 'as': 'board'},
    {'do': 'interact', 'target': 'board', 'asPlayer': True}, wait(0.4),
    {'do': 'type', 'field': '*', 'text': 'Open for testing', 'submit': True}, wait(0.3),
    {'do': 'lookAt', 'target': 'box', 'distance': 2},
    {'do': 'hook', 'name': 'testlab.recordCheckInventory'},
    wait(0.3),
    {'do': 'screenshot', 'name': 'feature-recording-in-game', 'clean': True},
    {'do': 'hook', 'name': 'testlab.recordStop'},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Record'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'feature-recorded-steps'},
    # the same setup saved as a case, in the Cases tab
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Cases'}}, wait(0.3),
    {'do': 'hook', 'name': 'testlab.saveCase', 'args': {'name': 'my-shop-setup', 'description': 'A chest of wood, a sign and a workbench', 'ui': True}, 'contains': 'Saved', 'label': 'saved through the Cases tab'}, wait(0.5),
    {'do': 'screenshot', 'name': 'feature-saved-case'},
    # Player 2: Bjornita joins as a second client and stands beside me
    {'do': 'hook', 'name': 'testlab.closeToolbox'},
    {'do': 'peerJoin', 'name': 'Bjornita', 'dx': 2.5, 'dz': 3.5, 'as': 'bjornita'}, wait(2.0),
    chk('peerConnected', 'Bjornita is connected', peer='Bjornita'),
    {'do': 'face', 'target': 'box'}, wait(0.6),
    {'do': 'screenshot', 'name': 'feature-player2-in-world', 'clean': True},
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Wards'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'feature-player2-wards-tab'},
    {'do': 'hook', 'name': 'testlab.closeToolbox'},
    {'do': 'peerLeave', 'name': 'Bjornita'},
    # the manual-check bar
    {'do': 'face', 'target': 'board'},
    {'do': 'hook', 'name': 'testlab.showCheckpoint', 'args': {'message': 'Does the sign say "Open for testing"?'}}, wait(0.5),
    {'do': 'screenshot', 'name': 'feature-manual-check-bar'},
    {'do': 'hook', 'name': 'testlab.showCheckpoint', 'args': {'clear': True}},
], desc='Pictures of the 0.3 features in use, for the README and the wiki.')

# ---------------------------------------------------------------- negative tests: the runner must catch these
T('N-01', 'a failing check fails the test', [
    chk('hasItem', 'a check that cannot pass', prefab='DragonEgg'),
], expectFailure='a check that cannot pass',
  desc='Negative test: passes only because the runner reports the failed check.')

T('N-02', 'a failing check without stop lets the steps go on', [
    chk('hasItem', 'first failing check', prefab='DragonEgg'),
    {'do': 'give', 'prefab': 'Wood', 'count': 1},
], [{'check': 'hasItem', 'prefab': 'Wood', 'label': 'the step after the failed check still ran'}], expectFailure='first failing check')

T('N-03', 'stop: true halts the test at a failed check', [
    chk('hasItem', 'stopping check', prefab='DragonEgg', stop=True),
    {'do': 'destroy', 'target': 'nothing-with-this-name'},
], expectFailure='stopping check', desc='If stop did not halt, the destroy step would fail with a different reason.')

T('N-04', 'a failing assert fails the test', [], [
    {'check': 'noItem', 'prefab': 'Wood', 'label': 'fine'},
    {'check': 'globalKey', 'key': 'drake_meta_never', 'label': 'an assert that cannot pass'},
], expectFailure='an assert that cannot pass')

T('N-05', 'a step that cannot be done fails the test', [
    {'do': 'give', 'prefab': 'NoSuchItemAnywhere'},
], expectFailure='could not give')

T('N-06', 'an impossible real-cost craft fails', [
    {'do': 'craft', 'prefab': 'AxeFlint', 'free': False},
], expectFailure='made nothing')

T('N-07', 'wait until a check that never comes true fails', [
    {'do': 'wait', 'until': {'check': 'hasItem', 'prefab': 'DragonEgg'}, 'timeout': 1.5},
], expectFailure='')

T('N-08', 'an unknown hook fails', [
    {'do': 'hook', 'name': 'testlab.noSuchHook'},
], expectFailure='noSuchHook')

T('N-09', 'the test timeout stops a stuck test', [
    wait(8),
], expectFailure='timed out', timeout=2)

T('N-10', 'a place the hammer refuses fails unless expect: false', [
    {'do': 'place', 'prefab': 'piece_workbench', 'free': False},
], expectFailure='')

T('N-11', 'hook compare mismatch fails', [
    {'do': 'hook', 'name': 'testlab.echo', 'args': {'value': 'yes'}, 'expect': 'no', 'label': 'echo mismatch'},
], expectFailure='echo mismatch')

T('N-12', 'expectFailure itself: a test expected to fail that passes is reported', [
    {'do': 'log', 'message': 'this test passes'},
], expectFailure='this text never appears in a reason', manualOnly=True,
  desc='Kept manual-only: it fails by design to show what a wrong expectFailure looks like.')

# ---------------------------------------------------------------- write
if os.path.isdir(OUT):
    shutil.rmtree(OUT)
os.makedirs(OUT)
with open(os.path.join(OUT, 'suite.json'), 'w', encoding='utf-8') as f:
    json.dump(suite, f, indent=2, ensure_ascii=False)
for tid, t in tests.items():
    with open(os.path.join(OUT, f'{tid}.json'), 'w', encoding='utf-8') as f:
        json.dump(t, f, indent=2, ensure_ascii=False)
print(f'{len(tests)} meta tests')
