"""Run independent authoring branches while preserving failed dependency gates."""
from __future__ import annotations

import os
from pathlib import Path

from .graph import digest, topological_order
from .runner import write_json


def run_batch(runner, targets):
    order = topological_order(runner.graph['nodes'], targets)
    runner.output.mkdir(parents=True, exist_ok=True)
    lock = runner.output / 'running.lock'
    with lock.open('x', encoding='utf-8') as stream:
        stream.write(str(os.getpid()))
    states = {}
    report = runner.output / 'batch-result.json'
    batch_id = runner.output.name+'-'+digest({'run':str(runner.output),'graph':runner.graph})[:12]

    def save(status):
        result = dict(status=status, productionReady=False,
            graphDigest=digest(runner.graph), targets=list(targets), nodes=states,
            completedTargets=sum(k in runner.results for k in targets),
            generatedCandidates=sum(r['op']=='generate' and not r['boundInput'] for r in runner.results.values()),
            failedNodes=sum(s['status']=='failed' for s in states.values()),
            blockedNodes=sum(s['status']=='blocked' for s in states.values()))
        write_json(report, result)
        write_json(runner.output / 'receipts.json', runner.results)
        return result

    def register(status):
        from .viewer_export import export_batch
        try:
            runner.viewer_batch = export_batch(runner.graph, runner.results,
                run_dir=runner.output, label=runner.output.name, status=status, batch_id=batch_id)
        except Exception as error:
            write_json(runner.output / 'viewer-export-failure.json', {'error':str(error)})
            print('VIEWER_EXPORT_FAILED', str(error), flush=True)

    try:
        for key in order:
            node = runner.graph['nodes'][key]
            blocked = [dep for dep in node['deps'] if dep not in runner.results]
            if blocked:
                states[key] = dict(status='blocked', dependencies=blocked)
                continue
            try:
                runner.execute_node(key)
                states[key] = dict(status=runner.events[-1]['status'], identity=runner.results[key]['identity'])
            except Exception as error:
                states[key] = dict(status='failed', error=str(error),
                    evidence=next((e.get('evidence') for e in reversed(runner.events) if e.get('node',key)==key and e['status']=='failed'),None))
                print('REJECTED', key, str(error)[:300], flush=True)
            save('running')
            if node['op'] in ('generate', 'gait_cycle', 'mantle'):
                register('running')
        result = save('authoring_rejected' if any(s['status'] in ('failed','blocked') for s in states.values()) else 'authoring_complete')
        register(result['status'])
        result['viewerBatch'] = getattr(runner, 'viewer_batch', None)
        write_json(report, result)
        return result
    finally:
        if len(states) != len(order):
            save('interrupted')
            register('interrupted')
        lock.unlink()
