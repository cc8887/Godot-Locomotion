// Web export chain (independent of the frozen Godot pipeline).
// Strips the 30 finger bones from the ALS rig and raw animation sources,
// renumbers the remaining 38-bone skeleton and emits a web-ready asset pack:
//   skeleton_38.json  - 38-bone rest skeleton (Godot/right-handed, meters)
//   clips/<id>.json   - per-clip 38-bone tracks (rotation + position, scale folded)
//   report.json       - float/byte accounting vs. the 68-bone source data
// Reads als_manifest.json + assets/config/raw_sequences only; writes nothing
// inside assets/generated/als_v4 so frozen digests stay valid.

import { readdir, readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';

const ROOT = process.cwd();
const RAW_DIR = path.join(ROOT, 'assets', 'config', 'raw_sequences');
const MANIFEST_PATH = path.join(ROOT, 'assets', 'generated', 'als_v4', 'als_manifest.json');
const OUT_DIR = path.join(ROOT, 'assets', 'generated', 'web_export');

const FINGER = /^(thumb|index|middle|ring|pinky)_/i;
const LOCO = /\/Base\/(Locomotion|BasePoses|Transitions)\//i;

// Mirrors AlsRawAnimationSourceCompiler.ConvertPose exactly:
// UE left-handed centimeters -> Godot right-handed meters.
const convertPos = ([x, y, z]) => [x * 0.01, -y * 0.01, z * 0.01];
const convertRot = ([x, y, z, w]) => [-x, y, -z, w];
const isConstant = (rows, width) => {
  if (rows.length <= 1) return true;
  const first = rows[0];
  for (let r = 1; r < rows.length; r++) {
    for (let c = 0; c < width; c++) {
      if (Math.abs(rows[r][c] - first[c]) > 1e-6) return false;
    }
  }
  return true;
};
const floatsOf = (rows, width) => rows.length * width;
// Constant channels fold to a single key (matches the C# compiler's count==1 rule).
const foldChannel = (rows, constant) => (constant && rows.length > 1 ? [rows[0]] : rows);

function fail(message) {
  console.error('WEB_EXPORT_FAILED: ' + message);
  process.exit(1);
}

async function main() {
  const manifest = JSON.parse(await readFile(MANIFEST_PATH, 'utf8'));
  if (!Array.isArray(manifest.skeletons) || manifest.skeletons.length === 0) fail('manifest has no skeletons');

  // Pick the largest skeleton (the ALS mannequin rig).
  const skeletons = manifest.skeletons
    .map((s, index) => ({ s, index }))
    .sort((a, b) => (b.s.metadata?.bones?.length ?? 0) - (a.s.metadata?.bones?.length ?? 0));
  const skeleton = skeletons[0].s;
  const metadata = skeleton.metadata;
  if (!metadata?.bones?.length) fail('skeleton metadata has no bones');

  const virtualNames = new Set((metadata.virtualBones ?? []).map(v => v.name.toLowerCase()));
  const allBones = metadata.bones;
  const physicalLogical = []; // logical indices in physical order
  allBones.forEach((bone, logical) => { if (!virtualNames.has(bone.name.toLowerCase())) physicalLogical.push(logical); });
  const logicalToPhysical = new Int32Array(allBones.length).fill(-1);
  physicalLogical.forEach((logical, physical) => { logicalToPhysical[logical] = physical; });

  // Physical bones minus finger bones, in physical order, renumbered.
  const keptLogical = physicalLogical.filter(logical => !FINGER.test(allBones[logical].name));
  const removedPhysical = physicalLogical.filter(logical => FINGER.test(allBones[logical].name));
  const keptSet = new Set(keptLogical);
  const newIndex = new Map(); // logical index -> web index
  keptLogical.forEach((logical, web) => newIndex.set(logical, web));
  const virtualBonesSource = (metadata.virtualBones ?? []).map(v => {
    const bone = allBones.find(b => b.name.toLowerCase() === v.name.toLowerCase());
    if (!bone) fail(`virtual bone ${v.name} missing from bones[]`);
    return { name: bone.name, source: v.source, target: v.target, translation: convertPos(bone.translation), rotation: convertRot(bone.rotation) };
  });
  const virtualNamesLower = new Set(virtualBonesSource.map(v => v.name.toLowerCase()));

  const webBones = keptLogical.map(logical => {
    const bone = allBones[logical];
    if (bone.parentIndex >= 0) {
      if (virtualNames.has(allBones[bone.parentIndex].name.toLowerCase())) fail(`physical bone ${bone.name} has a virtual parent`);
    }
    let parent = -1;
    if (bone.parentIndex >= 0) {
      if (!keptSet.has(bone.parentIndex)) fail(`bone ${bone.name} parents onto a removed finger bone`);
      parent = newIndex.get(bone.parentIndex);
    }
    return {
      name: bone.name,
      parent,
      translation: convertPos(bone.translation),
      rotation: convertRot(bone.rotation),
      scale: bone.scale,
    };
  });

  // Root sanity: exactly one -1 parent.
  if (webBones.filter(b => b.parent < 0).length !== 1) fail('renumbered skeleton does not have a single root');

  const skeletonOut = {
    format: 'als-web-skeleton/1',
    sourceSkeleton: skeleton.objectPath,
    boneCount: webBones.length,
    removedBoneCount: removedPhysical.length,
    removedBones: removedPhysical.map(l => allBones[l].name),
    virtualBones: virtualBonesSource,
    bones: webBones,
  };
  const skeletonText = JSON.stringify(skeletonOut);
  skeletonOut.sha256 = createHash('sha256').update(skeletonText).digest('hex');

  await mkdir(path.join(OUT_DIR, 'clips'), { recursive: true });
  await writeFile(path.join(OUT_DIR, 'skeleton_38.json'), JSON.stringify(skeletonOut, null, 2));

  const names = new Set(webBones.map(b => b.name.toLowerCase()));
  const files = (await readdir(RAW_DIR)).filter(f => f.endsWith('.json'));
  const report = {
    format: 'als-web-export-report/1',
    skeleton: { kept: webBones.length, removed: removedPhysical.length, sha256: skeletonOut.sha256 },
    clips: [], totals: {}, notes: [],
  };
  const totals = {
    all: { src: { pos: 0, rot: 0 }, out: { pos: 0, rot: 0, dynRot: 0, dynPos: 0 } },
    loco: { src: { pos: 0, rot: 0 }, out: { pos: 0, rot: 0, dynRot: 0, dynPos: 0 } },
  };
  let scaleChannelsFolded = 0, constantPosFolded = 0;

  for (const file of files) {
    const raw = JSON.parse(await readFile(path.join(RAW_DIR, file), 'utf8'));
    const assetId = file.slice(0, -5);
    const loco = LOCO.test(raw.source ?? '');
    const tracks = [];
    const virtualTracks = [];
    for (const track of raw.tracks) {
      const bucket = loco ? totals.loco : totals.all;
      bucket.src.pos += floatsOf(track.positions, 3);
      bucket.src.rot += floatsOf(track.rotations, 4);
      if (virtualNamesLower.has(track.bone.toLowerCase())) {
        const rotations = track.rotations.map(convertRot);
        const positions = track.positions.map(convertPos);
        const dynRot = !isConstant(rotations, 4);
        const dynPos = !isConstant(positions, 3);
        if (positions.length > 1 && !dynPos) constantPosFolded++;
        if (track.scales?.length > 1 && !isConstant(track.scales, 3)) fail(`unexpected animated scale on ${track.bone}`);
        virtualTracks.push({ bone: track.bone, positions: foldChannel(positions, !dynPos), rotations: foldChannel(rotations, !dynRot), dynRot, dynPos });
        continue;
      }
      if (!names.has(track.bone.toLowerCase())) {
        if (!FINGER.test(track.bone)) fail(`track ${track.bone} is neither kept, virtual nor a finger bone`);
        continue;
      }
      const rotations = track.rotations.map(convertRot);
      const positions = track.positions.map(convertPos);
      const dynRot = !isConstant(rotations, 4);
      const dynPos = !isConstant(positions, 3);
      const foldRot = foldChannel(rotations, !dynRot);
      const foldPos = foldChannel(positions, !dynPos);
      bucket.out.pos += floatsOf(foldPos, 3);
      bucket.out.rot += floatsOf(foldRot, 4);
      if (dynRot) bucket.out.dynRot += floatsOf(foldRot, 4); else if (rotations.length > 1) constantPosFolded++;
      if (dynPos) bucket.out.dynPos += floatsOf(foldPos, 3); else if (positions.length > 1) constantPosFolded++;
      if (track.scales?.length > 1 && !isConstant(track.scales, 3)) fail(`unexpected animated scale on ${track.bone}`);
      if (track.scales?.length > 1) scaleChannelsFolded++;
      tracks.push({ bone: track.bone, positions: foldPos, rotations: foldRot, dynRot, dynPos });
    }
    const clip = {
      assetId,
      source: raw.source,
      frameRate: [raw.frameRateNumerator, raw.frameRateDenominator],
      sampledKeyCount: raw.sampledKeyCount,
      playLength: raw.playLength,
      locomotionSubset: loco,
      tracks,
      virtualTracks,
    };
    await writeFile(path.join(OUT_DIR, 'clips', assetId + '.json'), JSON.stringify(clip));
    report.clips.push({ assetId, source: raw.source, locomotionSubset: loco, tracks: tracks.length, virtualTracks: virtualTracks.length });
  }

  const bytes = f => f * 4;
  const mb = f => (bytes(f) / 1048576).toFixed(2) + ' MB';
  report.totals = {
    note: 'f32 bytes; "trimmed" = 38-bone exports; "compact" = dynamic rotation channels only, folded constants skipped',
    allClips: {
      clips: files.length,
      source68: mb(totals.all.src.pos + totals.all.src.rot),
      trimmed38: mb(totals.all.out.pos + totals.all.out.rot),
      compact38: mb(totals.all.out.dynRot + totals.all.out.dynPos),
      floats: totals.all,
    },
    locomotionSubset: {
      source68: mb(totals.loco.src.pos + totals.loco.src.rot),
      trimmed38: mb(totals.loco.out.pos + totals.loco.out.rot),
      compact38: mb(totals.loco.out.dynRot + totals.loco.out.dynPos),
      floats: totals.loco,
    },
    foldedScaleTracks: scaleChannelsFolded,
  };
  const varyingFingerClips = ['ALS_N_to_CLF', 'ALS_CLF_to_N'];
  report.notes.push(
    'Scale channels were verified constant and are folded to identity on the web side.',
    `Finger motion exists only in: ${varyingFingerClips.join(', ')}; crouch-finger curl is lost by design in plan C.`,
    'Skeleton + clips are already converted to Godot/right-handed meters; no per-frame conversion needed on the web side.');
  await writeFile(path.join(OUT_DIR, 'report.json'), JSON.stringify(report, null, 2));

  console.log('WEB_EXPORT_OK clips=' + files.length +
    ' bones=' + webBones.length + '/' + (webBones.length + removedPhysical.length) +
    ' loco source68=' + report.totals.locomotionSubset.source68 +
    ' trimmed38=' + report.totals.locomotionSubset.trimmed38 +
    ' compact38=' + report.totals.locomotionSubset.compact38 +
    ' all source68=' + report.totals.allClips.source68 +
    ' trimmed38=' + report.totals.allClips.trimmed38 +
    ' compact38=' + report.totals.allClips.compact38);
}

main().catch(error => fail(error?.stack ?? String(error)));
