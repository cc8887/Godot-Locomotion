import fs from 'node:fs';

const [input, output] = process.argv.slice(2);
if (!input || !output) throw new Error('Expected exported settings JSON and formal output.');
const source = JSON.parse(fs.readFileSync(input, 'utf8'));
const text = source.curve.nativeText;
// FRichCurve and FRichCurveKey constructor defaults supply omitted fields.
// Reject other curve-level settings until their semantics are supported.
const line = text.split(/\r?\n/).find(l => l.trim().startsWith('FloatCurve='))?.trim();
const match = line?.match(/^FloatCurve=\(Keys=\(\((.*)\)\)\)$/);
if (!match) throw new Error('Unexpected native prediction curve serialization.');
const keys = match[1].split('),(').map(key => {
  const fields = Object.fromEntries(key.split(',').map(field => field.split('=')));
  const allowed = ['InterpMode', 'TangentMode', 'TangentWeightMode', 'Time', 'Value', 'ArriveTangent', 'LeaveTangent', 'ArriveTangentWeight', 'LeaveTangentWeight'];
  if (Object.keys(fields).some(k => !allowed.includes(k))) throw new Error('Unknown native curve key field.');
  const number = name => {
    const value = Number(fields[name] ?? 0);
    if (!Number.isFinite(value)) throw new Error('Nonfinite native curve key.');
    return value;
  };
  return { time: number('Time'), value: number('Value'), arriveTangent: number('ArriveTangent'), leaveTangent: number('LeaveTangent'),
    interpMode: fields.InterpMode ?? 'RCIM_Linear', tangentWeightMode: fields.TangentWeightMode ?? 'RCTWM_WeightedNone' };
});
fs.writeFileSync(output, JSON.stringify({ schemaVersion: 1, animationClass: source.animationClass, settings: source.settings,
  sweepChannel: source.sweepChannel, responseChannels: source.responseChannels,
  curve: { path: source.curve.path, preInfinityExtrap: 'RCCE_Constant', postInfinityExtrap: 'RCCE_Constant', keys,
    verification: source.curve.verification } }, null, 2) + '\n');
