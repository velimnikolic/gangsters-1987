#!/usr/bin/env python3
"""
Bakes Assets/Audio/** out of the Sonniss GDC sound library.

The library (C:/Users/N/sonnis) is a sampler bundle: a hundred-odd packs of two to
four files each, at 96 or 192 kHz and 24 bit, minutes long, named by their vendor's
UCS scheme. None of that is what a Unity project wants to carry, so nothing is
copied verbatim - every clip is cut to the useful moment, resampled to 44.1 kHz 16
bit, folded to mono where the game plays it in 3D, and levelled.

The manifest below IS the mapping from the game's roles to the library, and it is
the only place source paths appear. Re-running the script reproduces the folder
byte for byte from the library, so a bad cut is fixed by editing an offset here
rather than by hand-editing a WAV.

The bundle has no firearm in it, so the guns come from a second library: the Krotos
Studio free gun pack (KR016), extracted at C:/Users/N/krotos-gun-pack. Twelve takes
of three weapons - a 9mm, an AK-47 and a SPAS-12 - designed rather than field
recorded, which is why they are dry, short and mastered onto a subwoofer. They are
also the loudest material this project has ever had, and the guns are baked to stay
that way: see slam(). Provenance and licences for everything outside the Sonniss
bundle: Tools/audio/sources/SOURCES.md.

One role still has no recording at all: a POLICE SIREN, which is synthesized here as
a Federal Signal style wail, the American electronic siren of the period.

Usage:  python Tools/audio/import_sounds.py [library-root] [--dry]
"""

import sys
import math
from collections import namedtuple
from fractions import Fraction
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal

LIB = Path(sys.argv[1]) if len(sys.argv) > 1 and not sys.argv[1].startswith("-") \
    else Path("C:/Users/N/sonnis")
GUNS = Path("C:/Users/N/krotos-gun-pack")
SRC = Path(__file__).resolve().parent / "sources"   # what the bundle did not have
OUT = Path(__file__).resolve().parents[2] / "Assets" / "Audio"
DRY = "--dry" in sys.argv

RATE = 44100  # everything lands here; Unity re-encodes to Vorbis at import


# --------------------------------------------------------------------- helpers

def read(rel):
    """A path is looked for in each library in turn, so a manifest entry does not have
    to know which one a clip came from."""
    for root in (LIB, GUNS, SRC):
        path = root / rel
        if path.exists():
            break
    else:
        raise FileNotFoundError(f"{rel}\n  looked in: "
                                + "\n             ".join(str(r) for r in (LIB, GUNS, SRC)))
    try:
        x, sr = sf.read(str(path), always_2d=True, dtype="float64")
    except sf.LibsndfileError:
        x, sr = read_raw(path)
    return x, sr


def read_raw(path):
    """Fallback for the bundle's malformed WAVs - the Vox Bestiae pack writes a
    broken PEAK chunk that libsndfile refuses outright. Walk the RIFF chunks by
    hand, take fmt and data, ignore everything else."""
    import struct
    with open(path, "rb") as f:
        if f.read(12)[:4] != b"RIFF":
            raise ValueError(f"not a RIFF file: {path}")
        fmt = data = None
        while True:
            head = f.read(8)
            if len(head) < 8:
                break
            cid, size = struct.unpack("<4sI", head)
            if cid == b"fmt ":
                fmt = struct.unpack("<HHIIHH", f.read(size)[:16])
                f.seek(size - 16 + (size & 1), 1) if size > 16 else f.seek(size & 1, 1)
            elif cid == b"data":
                data = f.read(size)
                f.seek(size & 1, 1)
            else:
                f.seek(size + (size & 1), 1)
    if not fmt or data is None:
        raise ValueError(f"no fmt/data chunk: {path}")
    tag, ch, sr, _, _, bits = fmt
    if bits == 16:
        x = np.frombuffer(data, "<i2").astype(np.float64) / 32768.0
    elif bits == 24:
        b = np.frombuffer(data, np.uint8).reshape(-1, 3).astype(np.int32)
        v = (b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16))
        v[v >= 1 << 23] -= 1 << 24
        x = v.astype(np.float64) / (1 << 23)
    elif bits == 32 and tag == 3:
        x = np.frombuffer(data, "<f4").astype(np.float64)
    elif bits == 32:
        x = np.frombuffer(data, "<i4").astype(np.float64) / (1 << 31)
    else:
        raise ValueError(f"unhandled {bits}-bit tag {tag}: {path}")
    return x.reshape(-1, ch), sr


def cut(x, sr, t0, t1):
    a = max(0, int(t0 * sr))
    b = min(len(x), int(t1 * sr)) if t1 is not None else len(x)
    return x[a:b]


def mono(x):
    return x.mean(axis=1, keepdims=True)


def resample(x, sr, target=RATE):
    if sr == target:
        return x
    f = Fraction(target, sr).limit_denominator(4000)
    return signal.resample_poly(x, f.numerator, f.denominator, axis=0)


def respeed(x, ratio):
    """Play faster/slower - pitch and length together, the way a tape does. Used
    to take the motorcycle horns down into a car's register and to make a second
    engine loop that will not phase against the first."""
    f = Fraction(1 / ratio).limit_denominator(400)
    return signal.resample_poly(x, f.numerator, f.denominator, axis=0)


def butter(x, sr, kind, freq, order=4):
    sos = signal.butter(order, freq, btype=kind, fs=sr, output="sos")
    return signal.sosfiltfilt(sos, x, axis=0)


def fade(x, sr, fin=0.005, fout=0.02):
    y = x.copy()
    n = int(fin * sr)
    if n > 1:
        y[:n] *= np.linspace(0, 1, n)[:, None]
    n = int(fout * sr)
    if n > 1:
        y[-n:] *= np.linspace(1, 0, n)[:, None]
    return y


def decay(x, sr, tail):
    """Exponential tail shaper - keeps the attack, pulls the room in."""
    t = np.arange(len(x)) / sr
    return x * np.exp(-t / tail)[:, None]


def trim(x, sr, floor_db=-45.0, keep=0.15):
    """Length of the useful part: where the envelope last rises above floor_db under
    the peak, plus a little. These takes were recorded outdoors and their tails are
    real slapback rather than noise, so this rarely cuts a rifle short - what it does
    cut is the second of near-silence a submachine gun's shorter tail leaves behind."""
    b = int(sr * 0.01)
    k = len(x) // b
    if k < 2:
        return len(x)
    r = np.sqrt((x[:k * b, 0].reshape(k, b) ** 2).mean(axis=1))
    above = np.where(r > r.max() * 10 ** (floor_db / 20))[0]
    if len(above) == 0:
        return len(x)
    return min(len(x), int((above[-1] * 0.01 + keep) * sr))


def snap_cycles(seconds, f0):
    """Round a duration to a whole number of cycles of f0. A loop of a tonal
    recording - mains hum, an engine's firing pulse - only crossfades cleanly
    when head and tail arrive at the seam in phase; off by half a cycle and the
    fundamental cancels through the fade and the loop breathes."""
    return max(1, round(seconds * f0)) / f0


def loopify(x, sr, xfade=1.5):
    """Crossfade the tail into the head so the clip seams to itself. Result is
    xfade seconds shorter than the input."""
    n = int(xfade * sr)
    if n * 2 >= len(x):
        return fade(x, sr, 0.01, 0.01)
    head, body, tail = x[:n], x[n:-n], x[-n:]
    ramp = np.linspace(0, 1, n)[:, None]
    seam = tail * (1 - ramp) + head * ramp
    return np.concatenate([seam, body])


def level(x, peak_db=None, rms_db=None):
    y = x
    if rms_db is not None:
        rms = math.sqrt(float((y ** 2).mean())) or 1e-9
        y = y * (10 ** (rms_db / 20) / rms)
    if peak_db is not None:
        pk = float(np.abs(y).max()) or 1e-9
        target = 10 ** (peak_db / 20)
        if rms_db is None or pk > target:
            y = y * (target / pk)
    return y


def slam(x, drive=2.5, peak_db=-0.5):
    """LOUD, on purpose - the gun stage, and nothing else in the bake uses it.

    A gunshot carries about 20 dB of crest: peak-normalise it and the ear still
    hears it at its RMS while only the meter sees the peak, which is how a shot
    ends up quieter than the traffic it is fired over. So the report is pushed
    through a soft knee first - tanh, the curve a valve or a tape has - which
    flattens the transient, lifts everything under it and folds some of the pack's
    enormous low end up into harmonics a laptop speaker can actually move. Then it
    is normalised. Body comes up about 8 dB over a plain peak normalise; the file
    still never clips."""
    y = level(x, peak_db=0.0)
    y = np.tanh(drive * y) / math.tanh(drive)
    return level(y, peak_db=peak_db)


def write(rel, x, sr):
    path = OUT / rel
    x = np.clip(x, -1.0, 1.0)
    dur = len(x) / sr
    size = len(x) * x.shape[1] * 2 / 1048576
    print(f"  {rel:38s} {dur:6.2f}s {x.shape[1]}ch {size:5.2f}MB")
    if DRY:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    sf.write(str(path), x, sr, subtype="PCM_16")


def bed(rel, src, at, length, xfade=2.0, rms_db=-26.0, hp=None, lp=None,
        speed=None, tonal=None):
    """A looping stereo ambience: one calm window of a long field recording,
    seamed to itself. Windows were picked by scanning each recording for the
    stretch with the least level drift - see the offsets, they are not round.

    The peak ceiling is only a clip guard: a bed is levelled by its RMS, because
    what it has to hold steady against the other beds is its body, not its
    loudest gust."""
    x, sr = read(src)
    x = cut(x, sr, at, at + length + xfade + 1.0)
    x = resample(x, sr)
    if speed:
        x = respeed(x, speed)
    if hp:
        x = butter(x, RATE, "highpass", hp)
    if lp:
        x = butter(x, RATE, "lowpass", lp)
    if tonal:
        length, xfade = snap_cycles(length, tonal), snap_cycles(xfade, tonal)
    x = loopify(x[:int((length + xfade) * RATE)], RATE, xfade)
    write(rel, level(x, peak_db=-1.0, rms_db=rms_db), RATE)


def shot(rel, src, at, length, peak_db=-1.5, hp=None, lp=None, mono_out=True,
         fin=0.003, fout=0.03, speed=None, tail=None):
    """A one-shot. Mono by default: these all play through 3D sources, which
    collapse to mono anyway, so paying for two channels is paying twice."""
    x, sr = read(src)
    x = cut(x, sr, at, at + length)
    x = resample(x, sr)
    if mono_out:
        x = mono(x)
    if speed:
        x = respeed(x, speed)
    if hp:
        x = butter(x, RATE, "highpass", hp)
    if lp:
        x = butter(x, RATE, "lowpass", lp)
    if tail:
        x = decay(x, RATE, tail)
    x = fade(x, RATE, fin, fout)
    write(rel, level(x, peak_db=peak_db), RATE)


def engine(rel, src, at, length, speed=1.0, xfade=0.35, rms_db=-20.0, f0=44.0):
    """An engine loop: a stretch of steady RPM, seamed. The crossfade is short -
    a long one smears the firing pulses into mush - and both it and the loop
    length are snapped to whole firing cycles so the rumble does not cancel
    against itself at the wrap."""
    x, sr = read(src)
    x = cut(x, sr, at, at + length + xfade + 0.5)
    x = resample(x, sr)
    x = mono(x)
    if speed != 1.0:
        x = respeed(x, speed)
        f0 *= speed
    x = butter(x, RATE, "lowpass", 6000)   # a car heard outside has no fizz
    length, xfade = snap_cycles(length, f0), snap_cycles(xfade, f0)
    x = loopify(x[:int((length + xfade) * RATE)], RATE, xfade)
    write(rel, level(x, peak_db=-1.5, rms_db=rms_db), RATE)


# ------------------------------------------------------------------- the parts

WHIP = "David Dumais Audio - Melee Weapons Sound Effects Pack 2/WEAPWhip_WHIP Snap Crack 05_DDUMAIS_MWP2.wav"

# The guns, one per weapon the armoury actually sells. The pack holds three weapons
# in twelve takes, and a take is a whole string of fire rather than one report: the
# shots inside it stand 0.19 to 0.7 s apart, so each is cut out and becomes one
# variant. WINDOW is how much of the take a single report is allowed to take with
# it, always short of the next shot's attack - these are designed sounds and the
# whole report is over in a third of a second, so nothing is being cut off.
#
# TIMES are the attacks, found by transient search and then read off by hand. Only
# the reports with room behind them are listed; a shot whose neighbour arrives 70 ms
# later cannot be lifted out of a burst and is left in it.
KROTOS = "KR016 3 Types of Gun Shot Sound Free - AK47, SPAS12 and 9mm/"
LEAD = 0.012   # cut in ahead of the attack: a transient found in a 2 ms frame began
               # a frame or two earlier, and a shot missing its front edge is a thud

Take = namedtuple("Take", "src window times speed")
Take.__new__.__defaults__ = (None,)   # speed: a tape ratio, or nothing

GUNS_BY_KIND = {
    # Twin Pack Pistols / the default sidearm: the pack's 9mm, its lighter pistol
    # and its Desert Eagle, which between them are what a 1987 hood is carrying -
    # and the demo draws a new one per round, so a firefight is not one gun looped.
    "pistol": [Take(KROTOS + "9mm.wav", 0.30,
                    [0.012, 0.326, 0.664, 0.978, 1.502]),
               Take(KROTOS + "Light Pistol.wav", 0.27,
                    [0.418, 0.698, 2.654, 3.118]),
               Take(KROTOS + "Desert Eagle.wav", 0.27,
                    [0.020, 0.326, 1.350])],
    # SPAS-12. Three takes of the same 12 gauge: the single heavy blast, then the
    # shootout and the suppressive string, which have the pump in them.
    "shotgun": [Take(KROTOS + "SPAS12 Power.wav", 0.90, [0.012]),
                Take(KROTOS + "SPAS12 Shootout.wav", 0.50,
                     [0.008, 1.112, 2.510, 3.394]),
                Take(KROTOS + "SPAS12 Suppressive Fire.wav", 0.50,
                     [0.554, 1.672, 3.626])],
    # The pack's rapid fire - a pistol-calibre stream, which is the spray the
    # armoury describes. Four reports out of it have room enough to stand alone;
    # the gun fires every 0.2 s in play, so their tails lie over each other there
    # exactly as they do in the take.
    "machinepistol": [Take(KROTOS + "Rapid Fire.wav", 0.19,
                           [0.126, 1.072, 1.272, 1.804])],
    # AK-47, single shots. The rifle of the decade's drug wars.
    "rifle": [Take(KROTOS + "AK-47 Single Shots.wav", 0.40,
                   [0.000, 0.512, 1.022, 1.536, 2.328, 2.746, 3.444])],
    # A Thompson is a .45, and the pack has no .45 submachine gun. So it is the
    # rapid fire again, run at 0.86 of speed - the tape trick, which takes the
    # report down about two semitones and lengthens it into the slower, fatter
    # bark a drum gun has. Same four reports, and they do not sound the same.
    "tommygun": [Take(KROTOS + "Rapid Fire.wav", 0.19,
                      [0.126, 1.072, 1.272, 1.804], 0.86)],
}


def guns():
    for kind, takes in GUNS_BY_KIND.items():
        n = 0
        for take in takes:
            x, sr = read(take.src)
            x = mono(resample(x, sr))
            for t in take.times:
                y = cut(x, RATE, t - LEAD, t - LEAD + take.window)
                # The pack is mastered onto a subwoofer: most of a 9mm's energy sits
                # under 120 Hz and a fifth of one take is below 20 Hz, where no
                # speaker in a room moves and every bit of it eats the headroom the
                # report wants. Cut at 60 and the boom stays, the infrasound goes.
                y = butter(y, RATE, "highpass", 60)
                y = y[:trim(y, RATE)]
                y = fade(y, RATE, 0.0005, min(0.06, len(y) / RATE * 0.25))
                if take.speed:
                    y = respeed(y, take.speed)
                n += 1
                write(f"Weapons/{kind}_{n}.wav", slam(y), RATE)


def distant_shot(rel, src, at, length=1.10, peak_db=-4.0):
    """A report from the other side of the block. Distance takes the crack off a
    gun long before it takes the boom, so this is a real shot low-passed and run
    quiet - not a different recording pretending to be far away. It is levelled
    rather than slammed: what makes a shot read as far away is that it is the one
    sound on the street with no edge on it."""
    x, sr = read(src)
    y = mono(resample(cut(x, sr, at - LEAD, at + length), sr))
    y = butter(y, RATE, "highpass", 60)
    y = butter(y, RATE, "lowpass", 1800)
    y = fade(y, RATE, 0.004, 0.4)
    write(rel, level(y, peak_db=peak_db), RATE)


def footsteps(src, prefix, times, hp, length=0.19, peak_db=-6.0):
    for n, t in enumerate(times, 1):
        shot(f"People/{prefix}_{n}.wav", src, t - 0.015, length,
             hp=hp, peak_db=peak_db, fin=0.001, fout=0.05)


def siren():
    """Federal Signal style wail, synthesized: the bundle has no siren of any
    kind, and an American 1987 patrol car has exactly this one - a ~4.8 s sweep
    between roughly 700 and 1500 Hz, driven through a horn, so the harmonics
    matter as much as the fundamental. Built from an integer number of cycles at
    the sample rate, which makes it seamless by construction rather than by
    crossfade (a crossfade on a swept tone beats against itself)."""
    period = 4.8
    n = int(RATE * period)
    t = np.arange(n) / RATE
    sweep = 0.5 - 0.5 * np.cos(2 * np.pi * t / period)          # 0..1..0, smooth
    f = 700 + 800 * sweep
    phase = 2 * np.pi * np.cumsum(f) / RATE
    tone = (np.sin(phase)
            + 0.45 * np.sin(2 * phase)
            + 0.22 * np.sin(3 * phase)
            + 0.10 * np.sin(4 * phase))
    tone *= 0.85 + 0.15 * sweep                                  # louder up top
    x = tone[:, None]
    x = butter(x, RATE, "highpass", 400)                         # horn, not woofer
    x = butter(x, RATE, "lowpass", 7000)
    write("Police/siren_loop.wav", level(x, peak_db=-2.0), RATE)


# -------------------------------------------------------------------- manifest

def build():
    print("Ambience")
    bed("Ambience/city_day.wav",
        "Epic Stock Media - Public Spaces - Urban Life Exteriors/"
        "AMBTown_City Courtyard Calm Street Distant Traffic Children Playing 03_ESM_CPS.wav",
        at=129.0, length=30.0)
    bed("Ambience/city_night.wav",
        "Epic Stock Media - Public Spaces - Urban Life Exteriors/"
        "AMBUrbn_City Nightlife Ext Street In Reutersplatz German Walla Traffic 01_ESM_CPS.wav",
        at=0.4, length=30.0, rms_db=-28.0)
    # The hum a city makes four streets away: a downtown bed with its top rolled
    # off, so it reads as the roar rather than as any one car.
    bed("Ambience/traffic_hum.wav",
        "Epic Stock Media - Public Spaces - Basic Transportation Sounds/"
        "AMBTraf_Downtown Construction Traffic Light Walla 03 Distant Beep_ESM_CPS.wav",
        at=60.8, length=30.0, lp=2200, rms_db=-26.0)
    # The murmur slot the project has had empty since the people pack left.
    bed("Ambience/crowd_walla.wav",
        "Epic Stock Media - Public Spaces - Crowds Walla and Everyday Ambiences/"
        "AMBPubl_Metro Station Entrance Hall Dings Walla Footsteps 01 Women Shopping_ESM_CPS.wav",
        at=32.6, length=30.0, rms_db=-28.0)
    # Second murmur, low-passed hard: distant enough that no word survives, which
    # is the whole job of a murmur bed in a city that speaks English.
    bed("Ambience/crowd_walla_far.wav",
        "Epic Stock Media - Public Spaces - Crowds Walla and Everyday Ambiences/"
        "AMBPubl_Cruise Ship Walla Distant Adults Children Laughing Screaming 4_ESM_CPS.wav",
        at=40.0, length=24.0, lp=1800, rms_db=-30.0)
    bed("Ambience/rain_city.wav",
        "The Noisery - City Rain/"
        "RAINConc_Rain Medium Exterior Splatter City Traffic 6_The Noisery_City Rain.wav",
        at=118.0, length=24.0, rms_db=-24.0)
    bed("Ambience/wind_gusts.wav",
        "The Noisery - City Rain/"
        "WINDTonl_Wind Strong Gusts Hurricane Vents Rattle 06_The Noisery_City Rain.wav",
        at=96.2, length=24.0, rms_db=-28.0)
    bed("Ambience/park_trees.wav",
        "Epic Stock Media - Public Spaces - Storms Lakes Parks and Rural Nature Exteriors/"
        "AMBPark_Berlin City Humboldthain Park Strong Wind On Trees Foliage Traffic Wash 03_ESM_CPS.wav",
        at=121.8, length=24.0, rms_db=-28.0)
    # Connecticut crickets - the suburb after dark, and the one recording in the
    # bundle made on the coast this city is supposed to sit on.
    bed("Ambience/suburb_night.wav",
        "344 Audio - East Coast America Vol. 1/"
        "AMBSubn_Ambience, Forest Crickets, Birds, Connecticut 02_344 Audio_East Coast America.wav",
        at=20.6, length=24.0, rms_db=-28.0)
    # Streetlight and neon ballast hum: the sound of a 1987 street corner at 2am.
    # The recording hums at 100 Hz, which is a 50 Hz mains and therefore the wrong
    # continent - sped up by 6/5 it hums at 120, which is what an American ballast
    # does. Snapped to whole 120 Hz cycles so the tone survives its own seam.
    bed("Ambience/neon_hum.wav",
        "344 Audio - East Coast America Vol. 1/"
        "AMBSubn_Electricity Hum, Lightbulb,  Coil Pickup 01_344 Audio_East Coast America.wav",
        at=26.2, length=16.0, rms_db=-32.0, speed=1.2, tonal=120.0)
    bed("Ambience/harbor_industry.wav",
        "Victor Ermakov - Industrial Ambiences - Ship Repair Factory/"
        "AMBInd_Factory Hall Busy Alarm Machines Voices_CW.wav",
        at=3.0, length=24.0, rms_db=-27.0)
    bed("Ambience/harbor_crane.wav",
        "Victor Ermakov - Industrial Ambiences - Ship Repair Factory/"
        "MACHInd_Crane Onboard Ride Squeaks Motors_CW.wav",
        at=62.6, length=16.0, rms_db=-27.0)

    print("Traffic")
    # 17.9-25.4 s of the Mercury take: a stable ~44 Hz firing fundamental, which
    # is a V8 loafing. Everything either side of that window is changing RPM.
    MUSTANG = ("SoundBits - Cars - Mad Mustang Mercury/"
               "VEHCar_Various Driving at Slow Speed 15 06_SNDBTS_CRS-MMM.wav")
    engine("Traffic/engine_idle_a.wav", MUSTANG, at=17.9, length=7.5, f0=44.4)
    engine("Traffic/engine_idle_b.wav", MUSTANG, at=19.4, length=6.0, speed=0.93, f0=44.4)
    engine("Traffic/engine_diesel.wav",
           "Epic Stock Media - Public Spaces - Basic Transportation Sounds/"
           "BOATMotr_Diesel Engine Boat Idle Steady Rpm Hum Motor Mic One 01_ESM_CPS.wav",
           at=9.6, length=8.0, rms_db=-21.0, f0=25.4)

    # No horns: see the note in DemoAudio. The takes are still in the library.

    DOOR = ("SoundBits - Cars - Mad Mustang Mercury/"
            "VEHDoor_Car Foley Car Door Open and Close Exterior 04 Perspective A_SNDBTS_CRS-MMM.wav")
    shot("Traffic/car_door_open.wav", DOOR, at=0.00, length=1.10, peak_db=-4.0)
    shot("Traffic/car_door_close.wav", DOOR, at=1.72, length=0.90, peak_db=-3.0)

    shot("Traffic/tyre_skid.wav",
         "SoundBits - Cars - Mad Mustang Mercury/"
         "VEHSkid_Tire Skids on Gravel 15 06_SNDBTS_CRS-MMM.wav",
         at=5.30, length=1.90, peak_db=-3.0, fout=0.25)
    shot("Traffic/car_pass_by.wav",
         "SoundBits - Pass-By - Trains, Trucks & Cars 2/"
         "AMBTraf_Traffic Multiple Cars Passing By 25_SNDBTS_PB-TTC2.wav",
         at=2.40, length=3.20, peak_db=-6.0, fin=0.20, fout=0.40)
    shot("Traffic/truck_pass_by.wav",
         "SoundBits - Pass-By - Trains, Trucks & Cars 2/"
         "VEHFrght_Freight Truck Pass By 22_SNDBTS_PB-TTC2.wav",
         at=0.30, length=4.40, peak_db=-4.0, fin=0.15, fout=0.40)

    print("People")
    # Footsteps: no shoe pack in the bundle. The flip-flop takes are the only
    # walking recordings in it, so a step is the slap alone - the window is short
    # enough that the rubber's second flap never makes it in, and the high-pass
    # takes off the sandal's floppy body. Two surfaces, because the demo walks
    # people across pavement and across lot gravel.
    footsteps("TheWorkRoom - Flip Flops/FFW003.wav", "footstep_concrete",
              [0.63, 1.96, 6.22, 15.16, 20.00, 28.23], hp=180)
    footsteps("TheWorkRoom - Flip Flops/FFG002.wav", "footstep_gravel",
              [7.36, 8.79, 11.01, 23.50], hp=300, peak_db=-8.0)

    shot("People/whistle.wav",
         "SoundBits - Vox Hominis - Human Effort Voices/"
         "WHSTHmn_Whistle Male 06 09_SNDBTS_VH.wav",
         at=0.30, length=1.90, peak_db=-4.0, fout=0.15)
    shot("People/laugh_f.wav",
         "SoundBits - Vox Hominis - Human Effort Voices/"
         "VOXLaff_Laughing Female 02 06_SNDBTS_VH.wav",
         at=0.0, length=0.70, peak_db=-4.0)
    shot("People/laugh_m.wav",
         "Epic Stock Media - AAA Game Character Police Officer/"
         "VOXLaff_Police Officer Laugh Vocal Male 1.wav",
         at=0.05, length=1.60, peak_db=-4.0)
    shot("People/cough.wav",
         "Epic Stock Media - AAA Game Character Police Officer/"
         "HMNCough_Police Officer Cough Vocal Male 11.wav",
         at=0.05, length=1.30, peak_db=-4.0)
    shot("People/pant.wav",
         "SoundBits - Vox Hominis - Human Effort Voices/"
         "HMNBrth_Panting Male 02 04_SNDBTS_VH.wav",
         at=0.20, length=2.60, peak_db=-6.0, fout=0.20)

    # The crowd under fire. A gasp, a yell, a woman's scream and two hurts - the
    # bundle's yells come out of an anime pack, so they are cut to the shout and
    # rolled off above 9 kHz, which takes the sheen off without touching the panic.
    shot("People/panic_gasp.wav",
         "Epic Stock Media - AAA Game Character Police Officer/"
         "HMNBrth_Police Officer Gasp Vocal Male Shocked Alert 1.wav",
         at=0.02, length=0.50, peak_db=-3.0)
    shot("People/panic_yell_m.wav",
         "344 Audio - Anime Fight Voices Vol. 1/"
         "VOXMale_Male Adventurer, Active Yell 19_344 Audio_Anime Fight Voices.wav",
         at=0.0, length=0.45, lp=9000, peak_db=-3.0)
    shot("People/panic_scream_f.wav",
         "344 Audio - Anime Fight Voices Vol. 2/"
         "VOXFem_Anime, Warrior Elf Princess Aggressive Yell_344 Audio_Anime Fight Voices Vol 2_14.wav",
         at=0.10, length=1.10, lp=9000, peak_db=-3.0)
    shot("People/hurt_f.wav",
         "Epic Stock Media - AAA Game Character British Female Detective/"
         "VOXReac_British Detective Pain Vocal Female Dying High Grunt Breath_ESM_AAAGCBFD.wav",
         at=0.0, length=0.80, peak_db=-3.0)
    # The strain off the front of the choking take - a real male effort vocal,
    # which the bundle's only other male shouts (an anime pack) are not.
    shot("People/hurt_m.wav",
         "344 Audio - Cinematic Fight Vol. 1/"
         "FGHTGrab_Choking, Tension 03_344 Audio_Cinematic Fight Vol 1.wav",
         at=0.05, length=0.90, peak_db=-4.0, fout=0.15)
    shot("People/cry_f.wav",
         "SoundBits - Vox Hominis - Human Effort Voices/"
         "VOXCry_Crying Female 04 05_SNDBTS_VH.wav",
         at=0.20, length=2.40, peak_db=-6.0, fout=0.25)
    # The door a pedestrian goes through: a hotel stairwell door, which is the
    # only building door in the bundle and happens to be an American one. The take
    # is one open and one close six seconds apart, so it is cut in two.
    HOTEL_DOOR = ("InMotionAudio - USA Hotel/"
                  "DOORMetl_StairWellDoor01_InMotionAudio_USAHotel.wav")
    shot("People/door_open.wav", HOTEL_DOOR, at=0.0, length=1.10, peak_db=-5.0)
    shot("People/door_close.wav", HOTEL_DOOR, at=5.00, length=1.00, peak_db=-4.0)

    shot("People/dog_bark.wav",
         "344 Audio - Dog Vocalisations Vol. 1/"
         "ANMLDog_Dog Barks, Multiple, Indoors, Perspective,_344 Audio_Dog Vocalisations_02.wav",
         at=0.22, length=0.70, peak_db=-4.0)

    print("Police")
    siren()
    shot("Police/cop_shots_fired.wav",
         "Epic Stock Media - AAA Game Character Police Officer/"
         "VOXMale_Police Officer Custom Lines Vocal Male Emergency Shots Fired Need Backup.wav",
         at=0.05, length=2.00, peak_db=-3.0)
    # Dispatch chatter. The lines are British - the only radio voice in the
    # bundle - so they are band-limited to a 1987 set's 400-2800 Hz passband,
    # which is where a radio lives anyway and where an accent stops carrying.
    RADIO = "344 Audio - British Police Radio Vol. 1/"
    for n, (src, at, length) in enumerate([
        (RADIO + "VOXMale_Police Radio, Burglary, Calm, Update, Warning_344 Audio_British Police Radio.wav", 0.2, 6.0),
        (RADIO + "VOXMale_Police Radio, Car Theft, Relaxed, Update, Helpful_344 Audio_British Police Radio.wav", 0.2, 5.0),
        (RADIO + "VOXMale_Police, Vehicle, Calm, 'Standing By'_344 Audio_British Police Radio.wav", 0.1, 2.0),
    ], 1):
        shot(f"Police/radio_call_{n}.wav", src, at, length,
             hp=400, lp=2800, peak_db=-5.0, fin=0.02, fout=0.10)
    STATIC = ("Epic Stock Media - Fake Advertisements and Radio Sound Effects Audio Construction Kit/"
              "COMStatic_Radio Ham Loop Static Hum Active Powered On Garbled Noise 01_ESM_FA.wav")
    shot("Police/radio_squelch.wav", STATIC, at=1.10, length=0.35,
         hp=400, lp=3200, peak_db=-8.0, fin=0.002, fout=0.06)
    bed("Police/radio_static.wav", STATIC, at=5.0, length=8.0, xfade=0.8,
        hp=400, lp=3200, rms_db=-30.0)

    print("Weapons")
    guns()
    # Gunfire somewhere else in the city: the rifle and a shotgun, heard badly.
    distant_shot("Weapons/gunshot_far_1.wav",
                 KROTOS + "AK-47 Single Shots.wav", 3.444)
    distant_shot("Weapons/gunshot_far_2.wav",
                 KROTOS + "SPAS12 Power.wav", 0.012, length=1.40)
    # The near miss: the whip crack whole, which is exactly the sound a round
    # going past makes and is why it replaces the old pack's slap.
    shot("Weapons/bullet_crack.wav", WHIP, at=0.44, length=0.32,
         hp=350, peak_db=-4.0, fin=0.0005, fout=0.08)
    PUNCH = ("344 Audio - Cinematic Fight Vol. 1/"
             "FGHTImpt_4 x Punch, Body 02_344 Audio_Cinematic Fight Vol 1.wav")
    for n, t in enumerate([0.28, 1.28, 2.29, 3.29], 1):
        # High-passed at 70: the cinematic take puts most of its energy under
        # 125 Hz, which a body hit heard across a street simply does not have.
        shot(f"Weapons/punch_{n}.wav", PUNCH, at=t - 0.02, length=0.55,
             hp=70, peak_db=-3.0, fin=0.001, fout=0.12)
    shot("Weapons/bat_hit.wav",
         "David Dumais Audio - Melee Weapons Sound Effects Pack 2/"
         "SWSH_SWING IMPACTS Quick Heavy Weapon Swing To Thud Impact Var 01_DDUMAIS_MWP2.wav",
         at=0.0, length=1.40, hp=70, peak_db=-3.0)
    shot("Weapons/blade_swing.wav",
         "David Dumais Audio - Melee Weapons Sound Effects Pack 2/"
         "METLFric_SWING SCRAPE Swift Melee Weapon Swing With A Long Blade 14_DDUMAIS_MWP2.wav",
         at=0.0, length=1.20, peak_db=-4.0)
    shot("Weapons/explosion.wav",
         "Federico Soler - Effective Trailer Booms Vol. 2/EffectiveTrailer_Booms_Vol2_011.wav",
         at=0.0, length=3.50, peak_db=-2.0, fout=0.50)

    print("Ui")
    # The ledger is paper and bakelite, not glass. Every UI sound here is a
    # mechanism or a sheet - nothing designed, nothing synthetic.
    shot("Ui/click.wav",
         "Epic Stock Media - Board Game - Sound Set Kit for Tabletop and Digital Games/"
         "UIClick_UI Button Analog Vintage Double Click Neutral Dry Press 11_ESM_BG.wav",
         at=0.0, length=0.09, peak_db=-6.0, fout=0.02)
    shot("Ui/toggle_on.wav",
         "Sonic Bat - Vintage Radio/SBvr_Power Button 003.wav",
         at=0.05, length=0.55, peak_db=-6.0)
    shot("Ui/toggle_off.wav",
         "Sonic Bat - Vintage Radio/SBvr_Mode Select Wheel 006.wav",
         at=0.05, length=0.70, peak_db=-6.0)
    shot("Ui/page_turn.wav",
         "Cinematic Sound Design - Paper Foley/Encyclopedia Glossy Page Turn Muted.wav",
         at=0.0, length=0.50, peak_db=-8.0)
    shot("Ui/paper_rustle.wav",
         "Cinematic Sound Design - Paper Foley/Newspaper Static Foley Rummage.wav",
         at=0.30, length=1.60, peak_db=-8.0, fout=0.25)
    # The morning edition landing flat on the desk: a shorter, harder phrase from
    # the same Sonniss newspaper recording, kept separate from the map's rustle so
    # opening the paper has a recognisable beat of its own.
    shot("Ui/newspaper_slap.wav",
         "Cinematic Sound Design - Paper Foley/Newspaper Static Foley Rummage.wav",
         at=2.10, length=0.72, hp=55, peak_db=-4.0, fout=0.10)
    shot("Ui/map_open.wav",
         "Cinematic Sound Design - Paper Foley/A4 Printing Paper Rattle Page Turn Tail.wav",
         at=0.0, length=1.10, peak_db=-7.0, fout=0.20)
    shot("Ui/map_close.wav",
         "344 Audio - Antique Books/"
         "PAPRMisc_Antique Books Slow Page Turns 6_344 Audio_Antiques - Books.wav",
         at=0.60, length=1.10, peak_db=-7.0, fout=0.20)
    # The approve: a deep latch thunk, which is what a rubber stamp on a desk is.
    shot("Ui/stamp.wav",
         "Epic Stock Media - HD Lock And Mechanism Sound Design Kit/"
         "MECHLtch_Click Deep Mechanism Latch Button Nearfield Thunk 02_ESM_HDLM.wav",
         at=0.0, length=0.60, peak_db=-5.0)
    shot("Ui/type_key.wav",
         "344 Audio - Antique Typewriter/"
         "COMType_Typewriter Space Key, Typewriter 05_344 Audio_Antiques - Typewriter.wav",
         at=0.02, length=0.40, peak_db=-6.0)
    shot("Ui/type_carriage.wav",
         "344 Audio - Antique Typewriter/"
         "COMType_Typewriter Carriage Movement, Typewriter_344 Audio_Antiques - Typewriter_01.wav",
         at=0.10, length=1.60, peak_db=-5.0, fout=0.15)


if __name__ == "__main__":
    if not LIB.exists():
        sys.exit(f"library not found: {LIB}")
    print(f"library {LIB}\noutput   {OUT}\n")
    build()
    print("\ndone" + (" (dry run)" if DRY else ""))
