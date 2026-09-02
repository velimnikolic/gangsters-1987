using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>
    /// AIMING THE AMBUSH.
    ///
    /// A right click on a bin was one instant order and the men decided for themselves
    /// which face of it was the safe one. HELD, the same click is a question instead:
    /// the crew's places are laid on the ground in grey while the pointer swings left
    /// and right, and the order only leaves when the button does.
    ///
    /// Three rules hold it together:
    ///
    /// * <b>The preview and the order are one deal.</b> Both come out of
    ///   <see cref="DemoCrews.PlanAmbush"/> - the ambush's own dealer, read without
    ///   being given - so the grey squares cannot promise a shape the order will not
    ///   make.
    /// * <b>Letting go without turning is the old click.</b> The heading starts at what
    ///   <see cref="DemoCrews.AmbushHeading"/> would have chosen anyway, so a player who
    ///   never learns the gesture loses nothing. The double click has nothing to say to
    ///   this order either way: a man sent to cover RUNS there
    ///   (<see cref="DemoCrews.OrderAmbush(DemoCrews.Unit, DemoCrews.CoverAnchor)"/>).
    /// * <b>The camera lets go of the button for the length of the aim</b>
    ///   (<see cref="DemoCamera.RightDragTaken"/>). A right-drag anywhere else still
    ///   orbits; a right-drag that STARTED on something to get behind, with a crew
    ///   picked, is the aim and nothing else.
    /// </summary>
    public partial class CrewOverlay
    {
        /// <summary>Degrees of heading per pixel of pointer. Half a screen's width turns
        /// the crew most of the way round: the gesture is one flick, not a hand dragged
        /// back and forth for a quarter turn.</summary>
        const float AimDegreesPerPixel = 0.45f;

        /// <summary>How far the pointer has to travel before the aim is really an aim.
        /// Under it the heading stands at its default, so a hand that shook on an
        /// ordinary click cannot turn the crew round.</summary>
        const float AimSlackPx = 6f;

        /// <summary>How often the deal is re-read while the heading turns. Reading it
        /// walks a route to every candidate flank, so it is not a per-frame thing; a
        /// twelfth of a second is under the eye and a fraction of the work.</summary>
        const float AimEvery = 0.08f;

        /// <summary>How far apart the pips that show which way they will watch stand,
        /// and how many of them there are.</summary>
        const float AimWatchStep = 1.6f;
        const int AimWatchPips = 3;

        static readonly Color AimGhost = new Color(0.88f, 0.89f, 0.90f, 0.85f);
        static readonly Color AimWatch = new Color(0.88f, 0.89f, 0.90f, 0.30f);

        DemoCrews.CoverAnchor _aimAnchor;
        /// <summary>THE CREW THE GESTURE BEGAN ON. Selection stays live under a held
        /// right button - the left button still picks crews - so reading
        /// <c>_crews.Selected</c> at the release would order whoever happened to be
        /// picked by then, calling off THAT crew's fight instead. The aim is held
        /// against the crew it was started for, and a selection that moves off it calls
        /// the aim off rather than following it.</summary>
        DemoCrews.Unit _aimCrew;
        bool _aiming;
        Vector3 _aimHeading;      // the way they will WATCH, out of the anchor
        Vector3 _aimHeadingBase;  // what the crew would have chosen for itself
        float _aimNextAt;
        bool _aimPlanStale;
        readonly List<DemoCrews.Placement> _aimPlan = new List<DemoCrews.Placement>();
        List<MeshRenderer> _aimGhosts = new List<MeshRenderer>();
        MaterialPropertyBlock _aimTint;

        // ------------------------------------------------------------- the gesture

        /// <summary>Something to get behind under the pointer, and nothing with a better
        /// claim to the click on top of it. The chain is the release chain's own, in the
        /// release chain's order: a hold that stole the click from a card the player was
        /// opening would be worse than no gesture at all.</summary>
        bool CoverAimUnder(Vector2 px, out DemoCrews.CoverAnchor anchor)
        {
            anchor = default;
            if (_crews == null || _crews.Selected == null || _cam == null) return false;
            if (BookOpen || _ordersOpen || PointerOverUi()) return false;

            if (PickCarAt(px) != null) return false;
            var picked = PickAt(px);
            if (picked != null && picked.Faction != 0) return false;
            if (picked != null && picked == _crews.Selected &&
                TryGetOwnActions(picked, _enemyActions)) return false;
            if (FrontAt(px) != null) return false;
            if (PickWitnessAt(px) != null) return false;
            if (BusinessAt(px).IsValid) return false;

            var plane = new Plane(Vector3.up, new Vector3(0f, _crews.GroundY, 0f));
            var ray = _cam.ScreenPointToRay(px);
            if (!plane.Raycast(ray, out float enter)) return false;
            return DemoCrews.AnchorUnder(ray, ray.GetPoint(enter), out anchor);
        }

        /// <summary>The press that starts the aim. The heading begins where the crew
        /// would have put it, so the very next frame's preview is the order the old
        /// single click would have given.</summary>
        void BeginCoverAim(DemoCrews.CoverAnchor anchor)
        {
            _aimAnchor = anchor;
            _aimCrew = _crews.Selected;
            _aiming = true;
            _aimHeadingBase = _crews.AmbushHeading(_aimCrew, anchor);
            _aimHeading = _aimHeadingBase;
            _aimNextAt = 0f;
            _aimPlanStale = true;
            DemoCamera.RightDragTaken = true;
        }

        /// <summary>The pointer, while the button is down: left and right turn the
        /// heading about the thing, and the deal is re-read behind it.</summary>
        void TickCoverAim(Mouse mouse)
        {
            // the camera gets its button back only when the hand really lets go: an aim
            // called off by Escape under a still-pressed button must not become an orbit
            if (DemoCamera.RightDragTaken && !_aiming &&
                (mouse == null || !mouse.rightButton.isPressed))
                DemoCamera.RightDragTaken = false;
            if (!_aiming) return;
            if (mouse == null || _crews == null || _aimCrew == null || _aimCrew.Wiped ||
                _crews.Selected != _aimCrew || BookOpen)
            { EndCoverAim(order: false); return; }

            var px = mouse.position.ReadValue();
            float slack = AimSlackPx * (_canvas != null ? _canvas.scaleFactor : 1f);
            float dx = px.x - _rightDown.x;
            var want = Mathf.Abs(dx) > slack
                ? Quaternion.Euler(0f, dx * AimDegreesPerPixel, 0f) * _aimHeadingBase
                : _aimHeadingBase;
            if ((want - _aimHeading).sqrMagnitude > 1e-6f)
            {
                _aimHeading = want;
                _aimPlanStale = true;
            }

            if (_aimPlanStale && Time.unscaledTime >= _aimNextAt)
            {
                _aimNextAt = Time.unscaledTime + AimEvery;
                _aimPlanStale = false;
                _crews.PlanAmbush(_aimCrew, _aimAnchor, _aimHeading, _aimPlan);
            }
            DrawAimGhosts();
        }

        /// <summary>The release. The order goes out on the heading he stopped at, and
        /// the men run to it.</summary>
        void EndCoverAim(bool order)
        {
            if (!_aiming) return;
            _aiming = false;
            _aimPlan.Clear();
            HideAimGhosts();
            // THE PRESS IS SPENT, whichever way the aim ended. Without this an aim
            // called off by Escape left the press pending, and the release a moment
            // later fell through the ordinary chain and gave the order the player had
            // just cancelled.
            _rightPending = false;
            var crew = _aimCrew;
            _aimCrew = null;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed) DemoCamera.RightDragTaken = false;
            if (!order || _crews == null || crew == null || crew.Wiped ||
                _crews.Selected != crew || !_aimAnchor.Valid) return;

            // the stamp still goes down - a ground order right after this one may still
            // be the second half of a double click - but the ambush does not read it: a
            // man sent to cover runs there, always
            var up = mouse != null ? mouse.position.ReadValue() : _rightDown;
            _lastOrderAt = Time.unscaledTime;
            _lastOrderAtPx = up;

            if (_crews.OrderAmbush(crew, _aimAnchor, _aimHeading))
                ShowMark(_aimAnchor.At + Vector3.up * 0.6f, MarkTint);
            else if (_crews.AmbushRefusal != null)
                Refuse(_crews.AmbushRefusal);
        }

        // -------------------------------------------------------------- the drawing

        /// <summary>The crew's places, in the same world-space corner marker every man
        /// on this map already stands in, and in grey because none of it has happened
        /// yet. A short line of fainter pips out of the anchor says which way they will
        /// be watching, which is the half of the order the squares cannot show.</summary>
        void DrawAimGhosts()
        {
            int want = _aimPlan.Count + (_aimPlan.Count > 0 ? AimWatchPips : 0);
            EnsureAimGhosts(want);
            if (_aimTint == null) _aimTint = new MaterialPropertyBlock();

            int slot = 0;
            for (int i = 0; i < _aimPlan.Count; i++, slot++)
                PaintGhost(slot, _aimPlan[i].Spot, AimGhost);

            if (_aimPlan.Count > 0)
            {
                var flat = _aimHeading;
                flat.y = 0f;
                if (flat.sqrMagnitude > 1e-4f) flat.Normalize();
                for (int i = 0; i < AimWatchPips; i++, slot++)
                    PaintGhost(slot, _aimAnchor.At + flat * (AimWatchStep * (i + 1)), AimWatch);
            }

            for (int i = slot; i < _aimGhosts.Count; i++)
                if (_aimGhosts[i] != null && _aimGhosts[i].enabled) _aimGhosts[i].enabled = false;
        }

        void PaintGhost(int slot, Vector3 at, Color tint)
        {
            if (slot < 0 || slot >= _aimGhosts.Count) return;
            var ghost = _aimGhosts[slot];
            if (ghost == null) return;
            at.y = _crews != null ? _crews.GroundY : at.y;
            ghost.transform.SetPositionAndRotation(at, Quaternion.identity);
            _aimTint.Clear();
            _aimTint.SetColor(BaseColorId, tint);
            _aimTint.SetColor(ColorId, tint);
            ghost.SetPropertyBlock(_aimTint);
            if (!ghost.enabled) ghost.enabled = true;
        }

        void EnsureAimGhosts(int count)
        {
            // A hot reload keeps the children this overlay built and loses the list that
            // points at them, exactly as it does to the men's own markers - and a ghost
            // nothing points at can never be turned off again. Same cure as
            // EnsureTransientCaches: throw the orphans out and start the list afresh.
            if (_aimGhosts == null)
            {
                RemoveRuntimeChildren(transform, "ambush preview");
                _aimGhosts = new List<MeshRenderer>();
            }
            while (_aimGhosts.Count < count)
            {
                var go = new GameObject("ambush preview", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = GroundSquareMesh();
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = GroundSquareMaterial();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                mr.enabled = false;
                _aimGhosts.Add(mr);
            }
        }

        void HideAimGhosts()
        {
            for (int i = 0; i < _aimGhosts.Count; i++)
                if (_aimGhosts[i] != null && _aimGhosts[i].enabled) _aimGhosts[i].enabled = false;
        }
    }
}
