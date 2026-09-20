import fs from 'node:fs';
import assert from 'node:assert/strict';

// Pass the common log path prefix, ending before <hz>-<count>-<mode>.log.
const [prefix, ...extra] = process.argv.slice(2);
assert(prefix && extra.length === 0, 'Expected one dispatch matrix log prefix.');
const rows = [];
for (const hz of [30, 60, 120]) {
  for (const count of [1, 10]) {
    const pair = [];
    for (const mode of ['single', 'parallel']) {
      const file = `${prefix}${hz}-${count}-${mode}.log`;
      const log = fs.readFileSync(file, 'utf8');
      assert(!/ERROR:|SCRIPT ERROR:|Unhandled exception/i.test(log), `${file}: runtime error`);
      const matches = [...log.matchAll(/^REFACTORED_FOOT_DISPATCH_OK (.+)$/gm)];
      assert.equal(matches.length, 1, `${file}: expected one successful completion`);
      const values = Object.fromEntries(matches[0][1].trim().split(/\s+/).map(v => v.split('=')));
      assert.equal(Number(values.hz), hz, file);
      assert.equal(Number(values.characters), count, file);
      assert.equal(values.mode.toLowerCase(), mode, file);
      assert.equal(Number(values.cancellations), 2, file);
      assert.equal(Number(values.commit_holds), 1, file);
      assert(Number(values.frames) >= hz * 6 * count, `${file}: missing frames`);
      for (const field of ['air', 'crouch', 'locked', 'events', 'rays'])
        assert(Number(values[field]) > 0, `${file}: missing ${field} coverage`);
      for (const field of ['pose', 'root', 'result'])
        assert(/^[0-9A-F]{16}$/.test(values[field]), `${file}: missing ${field} digest`);
      const { mode: _, ...comparable } = values;
      pair.push(comparable);
    }
    assert.deepEqual(pair[0], pair[1], `${hz} Hz / ${count} characters: modes differ`);
    rows.push(pair[0]);
  }
}
console.log(JSON.stringify({ status: 'REFACTORED_DISPATCH_MATRIX_OK', runs: rows.length * 2, pairs: rows }, null, 2));
