using RoadDemo;
using UnityEngine;

namespace CoverDemo
{
    /// <summary>The Cover demo's long-run cover tally. The actual combat indicators
    /// live on DemoCrews, just as they do in every other scene.</summary>
    public sealed class CoverWatch : MonoBehaviour
    {
        DemoCrews _crews;
        DemoCamera _cam;

        float _fightSeconds, _coverSeconds, _duckedSeconds;
        int _standing, _engaged, _inCover, _ducked;

        public void Init(DemoCrews crews, DemoCamera cam)
        {
            _crews = crews;
            _cam = cam;
        }

        void Update() => Count(Time.deltaTime);

        void Count(float dt)
        {
            _standing = _engaged = _inCover = _ducked = 0;
            if (_crews == null) return;
            foreach (var unit in _crews.Units)
                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null) continue;
                    _standing++;
                    if (man.Target == null) continue;
                    _engaged++;
                    if (man.InCover) _inCover++;
                    if (man.Ducked) _ducked++;
                }

            _fightSeconds += _engaged * dt;
            _coverSeconds += _inCover * dt;
            _duckedSeconds += _ducked * dt;
        }

        void OnGUI()
        {
            if (LivingCity.UI.PersonnelAlmanac.IsOpen) return;
            float top = Screen.height / 1080f * (_cam != null ? _cam.hintTopPx : 104f) + 30f;
            bool indicators = _crews != null && _crews.IntentOverlay != null &&
                              _crews.IntentOverlay.IsVisible;
            string line = _standing + " standing, " + _engaged + " in a fight, " + _inCover +
                " of them behind something (" + _ducked + " down)   -   over the run, " +
                Share(_coverSeconds) + " of the fighting was done from cover, " +
                Share(_duckedSeconds) + " of it ducked" +
                (indicators ? "" : "   (indicators off - I)");
            GUI.Label(new Rect(12f, top, 1400f, 24f), line);
            if (!indicators) return;
            GUI.Label(new Rect(12f, top + 20f, 1400f, 24f),
                "boxes: what counts as cover   amber: walking to a flank   " +
                "green: behind it, shooting   blue: ducked   red: in the open   " +
                "violet: our car's route");
        }

        string Share(float seconds) =>
            _fightSeconds < 0.5f ? "-" : (100f * seconds / _fightSeconds).ToString("0") + "%";
    }
}
