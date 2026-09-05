"""Repeat live player-command trials in the already-open MiniCore editor.

Restarts Play for each attempt. Run only when the user authorizes replacing the
current live session. No UI automation, forced outcomes, money grants or teleports.
"""
import argparse
import hashlib
import json
import pathlib
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]


def record_build(folder, template):
    files = list((ROOT / 'Assets').rglob('*.cs'))
    files += list((ROOT / 'Library/ScriptAssemblies').glob('Assembly-CSharp*.dll'))
    hashes = {str(path.relative_to(ROOT)).replace('\\', '/'):
              hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(files)}
    (folder / 'build-sha256.json').write_text(json.dumps({
        'recorded_at': time.strftime('%Y-%m-%dT%H:%M:%S%z'),
        'template': hashlib.sha256(template.encode('utf-8')).hexdigest(),
        'files': hashes}, indent=2), encoding='utf-8')


def only_cli_timeouts(result):
    errors = [line for line in result.stdout.splitlines()
              if line.startswith(('error ', 'exception ', 'assert '))]
    return result.returncode == 1 and errors and all(
        'Failed to handle /api/exec request: Main thread operation timed out' in line
        for line in errors)


def command(*args):
    for attempt in range(6):
        proc = subprocess.run(['unity', 'command', *args, '--json'], cwd=ROOT,
                              text=True, encoding='utf-8', capture_output=True)
        try:
            result = json.loads(proc.stdout)
        except json.JSONDecodeError:
            result = {'success': False, 'errors': proc.stdout + proc.stderr}
        if result.get('success'):
            return result['data']['result']
        transient = any(text in str(result).lower() for text in
                        ('network error', 'connection', 'no unity editor', 'timed out'))
        # Play/stop are idempotent. Never replay a player-command eval after a lost reply.
        if args[0] not in ('editor_play', 'editor_stop', 'editor_status') or not transient or attempt == 5:
            raise RuntimeError(result)
        time.sleep(2)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('kind', choices=['arrest', 'court', 'bail', 'bail_skip', 'bail_refusal', 'collection_held', 'collection_held_auto', 'collection_held_backup', 'flee', 'fight', 'fight_win', 'collection', 'collection_auto', 'beating', 'killing', 'racket', 'racket_rendered', 'racket_overlap', 'arson', 'doorstep', 'roles', 'car', 'grenade', 'block_round_view', 'recruit_inside', 'streaming'])
    parser.add_argument('--passes', type=int, default=5)
    parser.add_argument('--max-attempts', type=int, default=20)
    parser.add_argument('--retry-combat-loss', action='store_true',
                        help='Retain failed victory attempts and retry ordinary crew defeats; other failures still stop.')
    parser.add_argument('--continue-on-traffic-failure', action='store_true',
                        help='Finish the gameplay flow after recording unrelated traffic stalls; such runs are not accepted passes.')
    parser.add_argument('--resume', type=pathlib.Path)
    parser.add_argument('--load-save', type=pathlib.Path,
                        help='Start each live repetition by loading a real pre-crime campaign save.')
    parser.add_argument('--load-leader', type=int,
                        help='Existing lieutenant to control in the loaded campaign.')
    args = parser.parse_args()
    if args.load_save and (args.kind not in ('bail', 'bail_skip', 'collection_held', 'collection_held_auto', 'collection_held_backup', 'court') or not args.load_leader or args.resume):
        parser.error('--load-save requires bail/bail_skip/collection_held/collection_held_auto/collection_held_backup/court and --load-leader; it cannot combine with --resume.')
    if args.load_save:
        args.load_save = args.load_save.resolve(strict=True)
        saved_day = json.loads(args.load_save.read_text(encoding='utf-8-sig'))['day']
    stamp = time.strftime('%Y%m%d-%H%M%S')
    suffix = '-loaded' if args.load_save else ''
    output = args.resume.resolve() if args.resume else ROOT / 'Temp' / 'player-trials' / (stamp + '-' + args.kind + suffix)
    template = (ROOT / 'Tools/play/player_trial.cs.txt').read_text(encoding='utf-8')
    passed = 0
    provisional = 0
    first_attempt = 1
    if args.resume:
        existing = sorted((p for p in output.iterdir() if p.is_dir() and p.name.isdigit()), key=lambda p: int(p.name))
        runtime_sha = hashlib.sha256((ROOT / 'Library/ScriptAssemblies/Assembly-CSharp.dll').read_bytes()).hexdigest()
        template_sha = hashlib.sha256(template.encode('utf-8')).hexdigest()
        for folder in existing:
            verdict = json.loads((folder / 'result.json').read_text(encoding='utf-8'))
            if verdict['kind'] != args.kind or verdict['status'] != 'PASS':
                raise ValueError('Resume requires completed passing attempts of the same scenario: ' + str(folder))
            evidence = json.loads((folder / 'build-sha256.json').read_text(encoding='utf-8'))
            if (evidence['files']['Library/ScriptAssemblies/Assembly-CSharp.dll'] != runtime_sha or
                    evidence['template'] != template_sha):
                raise ValueError('Resume cannot combine different runtime builds or scenario templates: ' + str(folder))
        passed = len(existing)
        first_attempt = max((int(p.name) for p in existing), default=0) + 1
    for attempt in range(first_attempt, args.max_attempts + 1):
        if (ROOT / 'Temp/player-campaign-stop').exists():
            print('Stopped between completed live attempts.', flush=True)
            return 2
        command('editor_stop')
        subprocess.run([sys.executable, 'Tools/play/console.py', '--mark'], cwd=ROOT, check=True)
        command('editor_play')
        folder = output / str(attempt)
        if args.continue_on_traffic_failure:
            folder.mkdir(parents=True, exist_ok=True)
            (folder / 'continue-traffic.flag').write_text('diagnostic flow only', encoding='utf-8')
        code = template.replace('__KIND__', json.dumps(args.kind)).replace('__ATTEMPT__', str(attempt)).replace('__FOLDER__', json.dumps(folder.as_posix()))
        # The builder populates while game time is paused. Check readiness without
        # advancing time or calling a private scene builder.
        for _ in range(60):
            try:
                ready = command('eval', '--code', 'return RoadDemo.TerritoryRuntime.Instance != null && LivingCity.Gameplay.PersonnelDirector.Instance != null && UnityEngine.Object.FindAnyObjectByType<RoadDemo.DemoCrews>() != null;')
            except RuntimeError as error:
                transient = ('timed out', 'no unity editor instances found', 'connection', 'network error')
                if not any(message in str(error).lower() for message in transient):
                    raise
                time.sleep(1)
                continue
            if ready.get('result') is True:
                break
            time.sleep(1)
        if args.load_save:
            folder.mkdir(parents=True, exist_ok=True)
            load_source = folder / 'load-save.cs'
            load_source.write_text('return LivingCity.Save.CampaignSave.LoadFromFile(' +
                                   json.dumps(args.load_save.as_posix()) + ');', encoding='utf-8')
            loaded = command('eval_file', '--file', str(load_source))
            if not loaded.get('success') or loaded.get('result') != '':
                raise RuntimeError('Player load was refused: ' + str(loaded))
            for _ in range(120):
                try:
                    ready = command('eval', '--code',
                        'return LivingCity.Save.CampaignSave.Pending == null && '
                        'RoadDemo.TerritoryRuntime.Instance != null && '
                        'UnityEngine.Object.FindAnyObjectByType<RoadDemo.DemoCrews>() != null && '
                        'LivingCity.Gameplay.PersonnelDirector.Instance != null && '
                        'LivingCity.Gameplay.OutfitDirector.Instance != null && '
                        f'LivingCity.Gameplay.OutfitDirector.Instance.Campaign.Day == {saved_day};')
                except RuntimeError as error:
                    transient = ('timed out', 'no unity editor instances found', 'connection', 'network error')
                    if not any(message in str(error).lower() for message in transient):
                        raise
                    time.sleep(1)
                    continue
                if ready.get('result') is True:
                    break
                time.sleep(1)
            else:
                raise TimeoutError('Loaded campaign did not restore its saved day and runtime.')
            (folder / 'resume.flag').write_text('Continue a real player save.', encoding='utf-8')
            (folder / 'resume-config.json').write_text(json.dumps({
                'leader': args.load_leader, 'source_save': str(args.load_save),
                'source_sha256': hashlib.sha256(args.load_save.read_bytes()).hexdigest(),
                'saved_day': saved_day}), encoding='utf-8')
        if args.kind in ('racket_rendered', 'racket_overlap', 'arson'):
            warmup_source = ROOT / 'Temp/loaded-door-ready.cs'
            warmup_source.write_text((ROOT / 'Tools/play/loaded_door_ready.cs.txt').read_text(encoding='utf-8'), encoding='utf-8')
            for warmup in range(180):
                ready = command('eval_file', '--file', str(warmup_source))
                if ready.get('result') is True:
                    break
                time.sleep(0.5)
            else:
                raise TimeoutError('The initial paused city did not finish loading the target storefronts.')
        startup = subprocess.run([sys.executable, 'Tools/play/console.py', '--trace'], cwd=ROOT,
                                 capture_output=True, text=True, encoding='utf-8')
        folder.mkdir(parents=True, exist_ok=True)
        record_build(folder, template)
        (folder / 'startup-console.txt').write_text(startup.stdout + startup.stderr, encoding='utf-8')
        # A read-only readiness query can time out while Unity is constructing the
        # city. Preserve it separately; it is not an exception from gameplay.
        startup_errors = [line for line in startup.stdout.splitlines()
                          if line.startswith(('error ', 'exception ', 'assert '))]
        if startup.returncode and (startup.returncode != 1 or not startup_errors or any(
                'Failed to handle /api/exec request: Main thread operation timed out' not in line
                for line in startup_errors)):
            raise RuntimeError('Startup errors: ' + startup.stdout)
        source = folder / 'scenario.cs'
        source.write_text(code, encoding='utf-8')
        result = command('eval_file', '--file', str(source))
        if not result.get('success'):
            raise RuntimeError(result)
        print(f'{args.kind} attempt {attempt} started: {folder}', flush=True)
        last = 0
        long_campaign = args.kind in ('court', 'bail', 'bail_skip', 'collection_auto', 'block_round_view',
                                     'collection_held', 'collection_held_auto', 'collection_held_backup')
        deadline = time.monotonic() + (10800 if long_campaign else 1200)
        while not (folder / 'result.json').exists():
            if time.monotonic() > deadline:
                raise TimeoutError('No trial verdict within the scenario time limit: ' + str(folder))
            if time.monotonic() - last > 30:
                last = time.monotonic()
                log = folder / 'actions.txt'
                if log.exists():
                    print(log.read_text(encoding='utf-8').splitlines()[-1], flush=True)
                memory = folder / 'memory.jsonl'
                if memory.exists():
                    with memory.open('rb') as stream:
                        stream.seek(max(0, memory.stat().st_size - 262144))
                        tail = stream.read().splitlines()
                    try:
                        sample = json.loads(tail[-1])
                        print(f"time {sample['t']:.1f}s, hour {sample['hour']:.2f}, safe {sample['safe']}, rounds {sample['rounds']}", flush=True)
                    except (IndexError, json.JSONDecodeError):
                        pass  # writer may still be appending this sample
            time.sleep(2)
        verdict = json.loads((folder / 'result.json').read_text(encoding='utf-8'))
        if args.load_save:
            verdict['loaded_campaign'] = json.loads((folder / 'resume-config.json').read_text(encoding='utf-8'))
            (folder / 'result.json').write_text(json.dumps(verdict), encoding='utf-8')
        runtime_log = folder / 'runtime-errors.jsonl'
        if runtime_log.exists():
            errors = [json.loads(line) for line in runtime_log.read_text(encoding='utf-8').splitlines() if line.strip()]
            gameplay_errors = [error for error in errors if
                'Failed to handle /api/exec request: Main thread operation timed out' not in error['message']]
            if gameplay_errors:
                verdict['status'] = 'FAIL'
                verdict['reason'] += ' Gameplay logged errors; see runtime-errors.jsonl.'
                (folder / 'result.json').write_text(json.dumps(verdict), encoding='utf-8')
        console = subprocess.run([sys.executable, 'Tools/play/console.py', '--trace'], cwd=ROOT,
                                 capture_output=True, text=True, encoding='utf-8')
        (folder / 'console.txt').write_text(console.stdout + console.stderr, encoding='utf-8')
        if console.returncode and not only_cli_timeouts(console):
            verdict['status'] = 'FAIL'
            verdict['reason'] += ' Console reported errors or an incomplete reading; see console.txt.'
            (folder / 'result.json').write_text(json.dumps(verdict), encoding='utf-8')
        elif console.returncode:
            verdict['observation_note'] = 'CLI observation queries timed out; preserved in console.txt. No gameplay exception reported.'
            (folder / 'result.json').write_text(json.dumps(verdict), encoding='utf-8')
        print(json.dumps(verdict), flush=True)
        if verdict['status'] == 'FAIL':
            if args.retry_combat_loss and args.kind == 'fight_win' and verdict['reason'] == 'The trial crew was wiped out before the requested outcome.':
                print('Combat loss retained; no acceptance credit. Trying a fresh run.', flush=True)
                continue
            return 1
        if verdict['status'] == 'PASS':
            passed += 1
        elif verdict['status'] == 'FLOW_PASS_TRAFFIC_FAIL' and args.continue_on_traffic_failure:
            passed += 1
            provisional += 1
        if passed >= args.passes:
            print(f'Completed: {passed} flows; {passed - provisional} accepted passes, {provisional} with traffic failures. {output}', flush=True)
            return 0
    print(f'INCOMPLETE: only {passed}/{args.passes} passes. {output}', flush=True)
    return 2


if __name__ == '__main__':
    raise SystemExit(main())
