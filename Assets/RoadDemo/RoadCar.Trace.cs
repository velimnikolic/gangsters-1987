using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>The black box: what a car writes into the drive trace (DriveTrace) -
    /// the faults it catches itself in, the sample rows, and the fields a manoeuvre
    /// event carries. Nothing here moves the car.</summary>
    public partial class RoadCar
    {
        // ------------------------------------------------------------------ the black box

        /// <summary>Who this car is in the trace: traffic, crew, police, lorry.</summary>
        public string Tag = "car";
        float _want;              // the speed the throttle was asked for this frame
        bool _wantHard;           // and whether it was asked for with both feet
        float _prevSpeed, _nextSample, _quietFor, _saidStall;
        Vector3 _prevPos;
        Manoeuvre _prevMan;
        bool _prevVia, _prevParked, _traced, _onDeck;
        int _traceEvents;

        /// <summary>One frame of this car in the trace: what it was asked to do, what it
        /// did, and anything that reads as a fault - a stop nobody asked for, braking
        /// harder than the profile allows, a step longer than the speed, a speed over
        /// the profile. Called from Place, which every path through the tick ends in.</summary>
        void TraceStep(float dt, Vector3 pos, float steer)
        {
            // whatever changed about what the car is doing: a swing out, a turn in the
            // road, a pull-in, a junction entered or left - the shape of the run
            if (_man != _prevMan) { DriveTrace.Event("man", "car " + Id, _prevMan + " -> " + _man, ManFields()); _prevMan = _man; }
            if ((Via != null) != _prevVia) { _prevVia = Via != null; DriveTrace.Event("man", "car " + Id, _prevVia ? "into the box" : "out of the box", ManFields()); }
            if (Parked != _prevParked) { _prevParked = Parked; DriveTrace.Event("man", "car " + Id, Parked ? "parked" : "away", ManFields()); }

            // on and off the elevated road. Two rows a journey, and between them the
            // whole question of whether the freeway is a road or an ornament: where a
            // car joined it, where it left it, and how far it rode.
            bool up = Road != null && Road.Elevated;
            if (up != _onDeck)
            {
                _onDeck = up;
                if (DriveTrace.On)
                {
                    var deck = DriveTrace.Take();
                    DriveTrace.Int(deck, "id", Id);
                    DriveTrace.Str(deck, "tag", Tag);
                    DriveTrace.Str(deck, "what", up ? "on" : "off");
                    DriveTrace.Num(deck, "v", Speed);
                    DriveTrace.Vec(deck, "p", pos);
                    DriveTrace.Row("deck", deck.ToString());
                }
            }

            // the first frame of the trace has no frame before it: the speed and the
            // place it would be compared against are nought, and every car would come
            // out of the gate braking from ten and teleporting from the origin
            bool first = !_traced;
            _traced = true;
            float acc = !first && dt > 1e-4f ? (Speed - _prevSpeed) / dt : 0f;
            float step = !first && _lastPlaced ? Vector3.Distance(pos, _prevPos) : 0f;
            float cruise = Cruise();

            // a stop nobody asked for: the car is not moving and not parked. Whether it
            // was ASKED to move is in the row (want), so a queue at a light reads apart
            // from a car that has simply died in the road.
            if (Mathf.Abs(Speed) < 0.3f && !Parked) _quietFor += dt; else { _quietFor = 0f; _saidStall = 0f; }

            // every branch here ends in a Fault(), whose message is a trace string and
            // whose only consumer is the trace - so the whole ladder is skipped when the
            // trace is closed, and none of the $"..." args (one per car per frame under a
            // stall or a hard brake) are built for a reader that is not listening
            if (DriveTrace.On)
            {
            if (first) { }
            // a car asked for nought is standing because it was told to (halted, parked,
            // waiting for its crew): only one that WANTS to move and is not moving is stuck
            else if (_want > 0.5f && _quietFor > 4f && _quietFor - _saidStall > 6f)
            {
                _saidStall = _quietFor;
                Fault("stall", $"still for {_quietFor:F1}s", acc, step, steer);
            }
            // braking harder than the profile is meant to allow
            else if (acc < -HardBrake * 1.15f && dt > 1e-4f)
                Fault("overbrake", $"{acc:F1} m/s2, hard={_wantHard}", acc, step, steer);
            // asked for a stop from cruising speed with nothing in front to explain it.
            // A throttle asked for nought brakes hard by design (TickRoad), so that is
            // not the fault being looked for here - an unexplained one is.
            else if (acc < -Brake * 1.05f && !_wantHard && _want > 0.01f &&
                     string.IsNullOrEmpty(Why) && Via == null)
                Fault("brake", $"{acc:F1} m/s2 with no reason given", acc, step, steer);
            // faster than the driver is allowed to go
            else if (Mathf.Abs(Speed) > cruise * 1.25f + 0.5f)
                Fault("speeding", $"{Speed:F1} over {cruise:F1}", acc, step, steer);
            // a step the speed does not account for: the car was moved, not driven
            else if (_lastPlaced && step > Mathf.Abs(Speed) * dt * 1.6f + 0.12f)
                Fault("jump", $"{step:F2} m in one frame at {Speed:F1} m/s", acc, step, steer);
            // the wheel wound right over AT SPEED: the line the car is following has a
            // kink in it. Slowly, full lock is just a tight corner - every junction has one.
            else if (Mathf.Abs(steer) > 33f && Mathf.Abs(Speed) > 7f)
                Fault("steer", $"{steer:F0} deg at {Speed:F1} m/s", acc, step, steer);
            }

            _prevSpeed = Speed;
            _prevPos = pos;

            if (DriveTrace.Now < _nextSample) return;
            _nextSample = DriveTrace.Now + DriveTrace.SampleEvery;
            var sb = Sample(acc, step, steer);
            DriveTrace.Row("car", sb.ToString());
        }

        System.Text.StringBuilder Sample(float acc, float step, float steer)
        {
            var sb = DriveTrace.Take();
            DriveTrace.Int(sb, "id", Id);
            DriveTrace.Str(sb, "tag", Tag);
            DriveTrace.Str(sb, "prof", Profile.Name);
            DriveTrace.Num(sb, "v", Speed);
            DriveTrace.Num(sb, "want", _want);
            DriveTrace.Num(sb, "acc", acc);
            DriveTrace.Num(sb, "step", step, "F3");
            DriveTrace.Num(sb, "steer", steer, "F0");
            DriveTrace.Int(sb, "road", Road != null ? Road.Index : -1);
            DriveTrace.Num(sb, "s", S);
            DriveTrace.Num(sb, "d", D);
            DriveTrace.Int(sb, "h", Heading);
            DriveTrace.Str(sb, "man", _man.ToString());
            DriveTrace.Bool(sb, "via", Via != null);
            DriveTrace.Bool(sb, "box", _inNode != null);
            DriveTrace.Bool(sb, "queue", InQueue);
            DriveTrace.Bool(sb, "parked", Parked);
            DriveTrace.Bool(sb, "goal", _hasGoal);
            DriveTrace.Num(sb, "quiet", _quietFor, "F1");
            DriveTrace.Num(sb, "held", _heldAtLine, "F1");
            DriveTrace.Int(sb, "lead", _leadId);
            DriveTrace.Num(sb, "lgap", _leadGap, "F1");
            DriveTrace.Str(sb, "why", Why);
            DriveTrace.Str(sb, "parking", ParkingReason);
            DriveTrace.Int(sb, "blockedBy", Deadlock.BlockerId);
            DriveTrace.Int(sb, "escapePeer", Deadlock.PeerId);
            DriveTrace.Vec(sb, "p", _pos);
            return sb;
        }

        /// <summary>Something the driving code should not have done. The whole state
        /// goes down with it, so the run can be read without guessing.</summary>
        void Fault(string kind, string what, float acc, float step, float steer)
        {
            if (_traceEvents > 4000) return;
            _traceEvents++;
            var sb = Sample(acc, step, steer);
            DriveTrace.Str(sb, "fault", kind);
            DriveTrace.Str(sb, "what", what);
            DriveTrace.Row("fault", sb.ToString());
        }

        /// <summary>Where the car was when a manoeuvre began, ended or was given up on.</summary>
        string ManFields()
        {
            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "tag", Tag);
            DriveTrace.Num(sb, "v", Speed);
            DriveTrace.Int(sb, "road", Road != null ? Road.Index : -1);
            DriveTrace.Num(sb, "s", S);
            DriveTrace.Num(sb, "d", D);
            DriveTrace.Str(sb, "man", _man.ToString());
            // which box he holds, and which way he means to go through it - the two
            // have to be the same thing, and a run where they were not is how a pair
            // of cars is let into one junction (they plan against the claim's name)
            if (_inNode != null) DriveTrace.Str(sb, "claim", Line(_inNode.Via));
            if (_via != null) DriveTrace.Str(sb, "plan", Line(_via));
            DriveTrace.Str(sb, "why", Why);
            DriveTrace.Str(sb, "passwhy", PassWhy);
            DriveTrace.Vec(sb, "p", _pos);
            return sb.ToString();
        }

        static string Line(Connector via) =>
            via == null ? "" :
            $"{via.From.Road.Index}/{via.From.Heading}->{via.To.Road.Index}/{via.To.Heading}#{via.Index}";

        /// <summary>Where the car is, for a log line.</summary>
        public string Describe()
        {
            if (Via != null) return $"[box {Via.From.Road.Index}/{Via.From.Heading}->{Via.To.Road.Index}/{Via.To.Heading} {Via.Kind} viaS={ViaS:F1}/{Via.Length:F1} inNode={(_inNode != null)} why={Why}]";
            return $"[road {(Road != null ? Road.Index : -1)} s={S:F1} d={D:F1} h={Heading} {DoingLine} committed={_committed} inNode={(_inNode != null ? _inNode.Via.From.Road.Index + "/" + _inNode.Via.From.Heading + "->" + _inNode.Via.To.Road.Index + "/" + _inNode.Via.To.Heading + " " + _inNode.Via.Kind + (_boxLeft ? " left" : " approaching") : "no")} why={Why}]";
        }

        /// <summary>Read by the traffic's spawner and the overlay: what the driver is doing.</summary>
        public string DoingLine => Deadlock.Active ? (Deadlock.Waiting ? "Letting blocked traffic clear" : "Easing past blocked traffic") :
            ParkingFailed ? ParkingReason :
            _hasGoal && _goalPark && _man == Manoeuvre.None ? ParkingReason : _man switch
        {
            Manoeuvre.Pass => "Going round",
            Manoeuvre.Crown => "On the crown",
            Manoeuvre.UTurn => "Turning round",
            Manoeuvre.PullIn => "Pulling in",
            Manoeuvre.PullOut => "Pulling out",
            Manoeuvre.Reverse => "Backing off",
            Manoeuvre.Aside => "Giving way",
            _ => Via != null ? "Crossing" : Parked ? "Parked" : "Driving",
        };
    }
}
