"""Infer periodic target gait phases before grounded-contact validation."""
import numpy as np
from scipy.signal import find_peaks

from .target_gait import PILOTS, DIRECTIONS, asset_name, _sole_geometry

POLICY = 'target-vertical-extrema-phase-v1'


def infer_phases(raw, skeleton, source, *, harmonics=3, low_fraction=.1,
                 minimum_excursion_cm=1., minimum_window_seconds=.06, contact_mode='flat_sole'):
    name = asset_name(source)
    if name not in PILOTS or PILOTS[name][1] == 'sprint_acceleration':
        raise ValueError('No periodic phase contract: ' + name)
    count = raw['sampledKeyCount']; period = count - 1
    length = float(raw['playLength'])
    if period < 8 or length <= 0:
        raise ValueError('Insufficient periodic phase samples')
    if (not 0 < low_fraction < .5 or not isinstance(harmonics, int)
            or isinstance(harmonics, bool) or not 1 <= harmonics <= period // 2):
        raise ValueError('Invalid phase smoothing/band parameters')
    points, velocity, _ = _sole_geometry(raw, skeleton, PILOTS[name][0], DIRECTIONS[name])
    if contact_mode not in ('flat_sole','toe'):
        raise ValueError('Unknown phase contact mode: '+contact_mode)
    heights = points[:-1, :, 1, 2] if contact_mode=='toe' else points[:-1, :, :, 2].mean(axis=2)
    if not np.isfinite(heights).all():
        raise ValueError('Non-finite vertical phase signal')
    contacts = np.zeros((count, 2), dtype=bool); sides = []; offsets = []
    for side, label in enumerate(('left', 'right')):
        signal = heights[:, side]
        spectrum = np.fft.rfft(signal); spectrum[harmonics+1:] = 0
        smooth = np.fft.irfft(spectrum, n=period)
        amplitude = float(np.ptp(smooth))
        if amplitude < minimum_excursion_cm:
            raise ValueError('No measurable swing excursion: ' + label)
        extrema = {}
        for kind, values in (('maximum', smooth), ('minimum', -smooth)):
            indices, props = find_peaks(np.tile(values, 3),
                prominence=max(.2 * amplitude, .25), distance=max(1, period//4))
            ranked = sorted([(float(prom), int(i-period)) for i, prom in
                             zip(indices, props['prominences']) if period <= i < 2*period], reverse=True)
            if not ranked or len(ranked) > 1 and ranked[1][0] >= .6 * ranked[0][0]:
                raise ValueError('Ambiguous vertical ' + kind + ': ' + label)
            extrema[kind] = ranked[0][1]
        trough = extrema['minimum']
        low = smooth <= smooth[trough] + low_fraction * amplitude
        if contact_mode=='toe':
            # A forefoot may stay low through a fast toe-off. Restrict its
            # repair window by travel-corrected speed, without a ground plane.
            low &= np.linalg.norm(velocity[:-1,side,1,:2],axis=-1)<=25.
            if not low[trough]:
                raise ValueError('Forefoot trough has no slow support candidate: '+label)
        first = last = trough
        while low[(first-1) % period] and trough-first < period-1:
            first -= 1
        while low[(last+1) % period] and last-first < period-1:
            last += 1
        if (last-first) * length/period < minimum_window_seconds or last-first >= period-1:
            raise ValueError('Unresolved vertical low phase: ' + label)
        frames = np.arange(first, last+1) % period
        offsets.append((first-trough, last-trough))
        sides.append(dict(side=label, troughFrame=trough, peakFrame=extrema['maximum'],
            excursionCm=amplitude, windowStartFrame=first % period,
            windowEndFrame=last % period, windowFrames=frames.tolist(),
            windowSeconds=(last-first)*length/period, smoothedHeightCm=smooth.tolist(),
            measuredHeightCm=signal.tolist()))
    gap = abs(sides[0]['troughFrame']-sides[1]['troughFrame'])/period
    if not .35 <= gap <= .65:
        raise ValueError('Vertical extrema do not alternate: ' + str(gap))
    # Use the overlap of the two measured low bands relative to each trough.
    # Different plateau widths must not shift one foot's inferred landing by
    # several frames when the extrema themselves alternate consistently.
    first = max(v[0] for v in offsets); last = min(v[1] for v in offsets)
    if (last-first)*length/period < minimum_window_seconds:
        raise ValueError('No common reliable trough-relative phase window')
    for side, row in enumerate(sides):
        row['measuredLowBandFrames'] = row['windowFrames']
        frames = (row['troughFrame']+np.arange(first, last+1)) % period
        contacts[frames, side] = True
        row.update(windowStartFrame=int(frames[0]), windowEndFrame=int(frames[-1]),
                   windowFrames=frames.tolist(), windowSeconds=(last-first)*length/period)
    contacts[-1] = contacts[0]
    return dict(policy=POLICY, passed=True, purpose='phase candidates for IK, not grounded-contact acceptance',
        source=source, sampledKeyCount=count, playLengthSeconds=length,
        independentOfGroundHeight=True, independentOfAuthoredContactCurves=True,
        smoothingHarmonics=harmonics, lowBandFraction=low_fraction,contactMode=contact_mode,
        minimumExcursionCm=minimum_excursion_cm, minimumWindowSeconds=minimum_window_seconds,
        troughPhaseGap=gap, windowPolicy='common-trough-relative-measured-low-band',
        sides=sides, candidateContacts=contacts.astype(int).tolist())
