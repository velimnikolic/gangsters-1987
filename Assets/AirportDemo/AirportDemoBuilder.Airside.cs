using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo
{
    // The paint and the lights - what actually makes the difference between a strip
    // of tarmac and a runway. Dimensions are the FAA's own (AC 150/5340-1, visual
    // runway): a 36 m centreline stripe with a 24 m gap, eight threshold bars 45 m
    // long and 1.75 m wide, 18 m figures for the designator, the aiming point 300 m
    // in, the holding position 75 m out from the runway centreline.
    //
    // An airport is recognised by its markings and its spacings long before it is
    // recognised by its buildings, so this file is where the realism is spent.
    public partial class AirportDemoBuilder
    {
        readonly List<Transform> _windsocks = new List<Transform>();

        // ------------------------------------------------------------ the runway

        void PaintRunway()
        {
            float half = RunwayHalf;
            float w = AirportSpec.RunwayHalfWidth;
            float y = AirportSpec.MarkY;
            var white = new Painter();
            var rubber = new Painter();

            // side stripes, their outer edge on the pavement edge
            white.Rect(-half, half, w - AirportSpec.EdgeStripeWidth, w, y);
            white.Rect(-half, half, -w, -w + AirportSpec.EdgeStripeWidth, y);

            // centreline, symmetric about the middle of the runway
            float pitch = AirportSpec.CentrelineStripe + AirportSpec.CentrelineGap;
            float cw = AirportSpec.CentrelineWidth * 0.5f;
            // the middle stripe straddles the midpoint; every other one is laid in
            // pairs either side of it, so the pattern is symmetric about the field
            white.Rect(-AirportSpec.CentrelineStripe * 0.5f, AirportSpec.CentrelineStripe * 0.5f, -cw, cw, y);
            for (float x = AirportSpec.CentrelineStripe * 0.5f + AirportSpec.CentrelineGap; x < half - 60f; x += pitch)
            {
                white.Rect(x, x + AirportSpec.CentrelineStripe, -cw, cw, y);
                white.Rect(-x - AirportSpec.CentrelineStripe, -x, -cw, cw, y);
            }

            for (int end = 0; end < 2; end++)
            {
                float sign = end == 0 ? -1f : 1f;              // -1 the west threshold (09), +1 the east (27)
                float thr = sign * half;

                // the threshold bar: eight stripes, four either side of the centreline
                float x0 = thr + sign * AirportSpec.ThresholdOffset;
                float x1 = x0 + sign * AirportSpec.ThresholdStripeLength;
                for (int i = 0; i < AirportSpec.ThresholdStripes; i++)
                {
                    int side = i < AirportSpec.ThresholdStripes / 2 ? -1 : 1;
                    int k = i < AirportSpec.ThresholdStripes / 2 ? (AirportSpec.ThresholdStripes / 2 - 1 - i) : (i - AirportSpec.ThresholdStripes / 2);
                    float inner = AirportSpec.ThresholdCentreGap * 0.5f
                                  + k * (AirportSpec.ThresholdStripeWidth + AirportSpec.ThresholdStripeGap);
                    float z0 = side * inner, z1 = side * (inner + AirportSpec.ThresholdStripeWidth);
                    white.Rect(Mathf.Min(x0, x1), Mathf.Max(x0, x1), Mathf.Min(z0, z1), Mathf.Max(z0, z1), y);
                }

                // the designator, reading the way the pilot sees it on approach
                string number = sign < 0f ? "09" : "27";
                float yaw = sign < 0f ? 90f : 270f;
                float dx = thr + sign * (AirportSpec.DesignatorOffset + AirportSpec.DesignatorHeight * 0.5f);
                PaintLegend(white, number, new Vector3(dx, 0f, 0f), AirportSpec.DesignatorHeight, yaw, y);

                // the aiming point: a bar either side, 300 m in
                float ax = thr + sign * AirportSpec.AimingPointFrom;
                float axEnd = ax + sign * AirportSpec.AimingBarLength;
                for (int s = -1; s <= 1; s += 2)
                {
                    float z0 = s * AirportSpec.AimingBarInner;
                    float z1 = s * (AirportSpec.AimingBarInner + AirportSpec.AimingBarWidth);
                    white.Rect(Mathf.Min(ax, axEnd), Mathf.Max(ax, axEnd), Mathf.Min(z0, z1), Mathf.Max(z0, z1), y);
                }

                // the touchdown zone, black with rubber - the one place on an airfield
                // that is always dirty, and the surest sign it is used
                for (int i = 0; i < 7; i++)
                {
                    float bx = thr + sign * (120f + i * 26f);
                    float spread = 5.5f + i * 0.8f;
                    rubber.Rect(bx - 9f, bx + 9f, -spread, spread, y - 0.004f);
                }
            }

            white.Emit("Runway markings", _whitePaint, _markingRoot);
            rubber.Emit("Touchdown rubber", _rubberMat, _markingRoot);
        }

        // ------------------------------------------------------------ the taxiways

        void PaintTaxiways()
        {
            float half = RunwayHalf;
            float tz = AirportSpec.TaxiwayZ, th = AirportSpec.TaxiwayHalf;
            float y = AirportSpec.MarkY;
            float cw = AirportSpec.TaxiCentrelineWidth * 0.5f;
            var yellow = new Painter();

            // the parallel taxiway's centreline, unbroken from end to end
            yellow.Rect(-half - 18f, half + 18f, tz - cw, tz + cw, y);
            // and its edge markings: a double dashed line at the pavement edge
            for (int s = -1; s <= 1; s += 2)
            {
                float z = tz + s * (th - 0.15f);
                yellow.Dashes(new Vector3(-half, 0f, z), new Vector3(half, 0f, z), 0.15f, 7.5f, 7.5f, y);
                yellow.Dashes(new Vector3(-half, 0f, z + s * 0.3f), new Vector3(half, 0f, z + s * 0.3f), 0.15f, 7.5f, 7.5f, y);
            }

            foreach (float cx in AirportSpec.ConnectorX)
            {
                float x = Mathf.Clamp(cx, -half + 30f, half - 30f);
                // the connector's own centreline, from the runway out to the taxiway,
                // with the curve at each end drawn as a short diagonal
                yellow.Rect(x - cw, x + cw, AirportSpec.RunwayHalfWidth + 8f, tz - 8f, y);
                yellow.Dashes(new Vector3(x, 0f, AirportSpec.RunwayHalfWidth + 8f), new Vector3(x, 0f, 0f), 0.15f, 100f, 0f, y);
                yellow.Dashes(new Vector3(x, 0f, tz - 8f), new Vector3(x, 0f, tz), 0.15f, 100f, 0f, y);

                // the holding position: two solid lines on the side the aeroplane
                // holds, two dashed on the runway side of them
                float hz = AirportSpec.HoldShortZ;
                float bw = AirportSpec.HoldBarWidth, gap = AirportSpec.HoldBarGap;
                float x0 = x - th - 0.5f, x1 = x + th + 0.5f;
                yellow.Rect(x0, x1, hz + gap, hz + gap + bw, y);
                yellow.Rect(x0, x1, hz + gap * 2f + bw, hz + gap * 2f + bw * 2f, y);
                yellow.Dashes(new Vector3(x0, 0f, hz - gap - bw * 0.5f), new Vector3(x1, 0f, hz - gap - bw * 0.5f), bw, 0.9f, 0.9f, y);
                yellow.Dashes(new Vector3(x0, 0f, hz - gap * 2f - bw * 1.5f), new Vector3(x1, 0f, hz - gap * 2f - bw * 1.5f), bw, 0.9f, 0.9f, y);
            }

            // the taxilanes onto the ramp
            foreach (float cx in AirportSpec.ApronEntryX)
                yellow.Rect(cx - cw, cx + cw, tz, AirportSpec.ApronZ0 + 6f, y);

            yellow.Emit("Taxiway markings", _yellowPaint, _markingRoot);
        }

        // ------------------------------------------------------------ the ramp

        void PaintApron()
        {
            float y = AirportSpec.MarkY;
            var yellow = new Painter();
            var white = new Painter();
            float cw = AirportSpec.TaxiCentrelineWidth * 0.5f;

            // the ramp's own taxilanes: one along the front of the stands, one down
            // each row of tie-downs
            float lane = AirportSpec.ApronZ0 + 12f;
            yellow.Rect(AirportSpec.ApronX0 + 6f, AirportSpec.ApronX1 - 6f, lane - cw, lane + cw, y);

            // the tie-down rows: a tee at every stand and a lane down each row
            for (int row = 0; row < AirportSpec.TieDownRows; row++)
            {
                float z = AirportSpec.TieDownRowZ0 + row * AirportSpec.TieDownRowPitch;
                yellow.Rect(AirportSpec.TieDownX0 - 6f, AirportSpec.TieDownX1 + 6f, z - 13f - cw, z - 13f + cw, y);
                for (float x = AirportSpec.TieDownX0; x <= AirportSpec.TieDownX1 + 0.1f; x += AirportSpec.TieDownPitch)
                {
                    // the tee an aeroplane's nosewheel is parked on
                    yellow.Rect(x - 0.12f, x + 0.12f, z - 3f, z + 3f, y);
                    yellow.Rect(x - 2.2f, x + 2.2f, z + 2.8f, z + 3.2f, y);
                }
            }

            // the commuter stands: a lead-in line from the ramp lane, the stop bar the
            // nosewheel is brought up to, and the stand's number
            for (int i = 0; i < AirportSpec.CommuterStandX.Length; i++)
            {
                float sx = AirportSpec.CommuterStandX[i];
                float stop = AirportSpec.CommuterStandZ;
                yellow.Rect(sx - cw, sx + cw, lane, stop, y);
                yellow.Rect(sx - 3.5f, sx + 3.5f, stop, stop + 0.3f, y);
                PaintLegend(yellow, (i + 1).ToString(), new Vector3(sx + 6.5f, 0f, stop - 5f), 3.2f, 180f, y);
                // and the safety line a wingtip must stay inside
                white.Rect(sx - 9f, sx + 9f, stop - 14f, stop - 13.8f, y);
            }

            // the helipad: the circle a helicopter comes to, and the H in the middle
            var pad = new Vector3(AirportSpec.HelipadX, 0f, AirportSpec.HelipadZ);
            white.Ring(pad, AirportSpec.HelipadCircle * 0.5f, 0.5f, y);
            white.Rect(pad.x - 2.4f, pad.x - 1.6f, pad.z - 3f, pad.z + 3f, y);
            white.Rect(pad.x + 1.6f, pad.x + 2.4f, pad.z - 3f, pad.z + 3f, y);
            white.Rect(pad.x - 2.4f, pad.x + 2.4f, pad.z - 0.4f, pad.z + 0.4f, y);

            // the service road: a white edge either side, dashed, so a driver on the
            // ramp knows where he may be and an aeroplane knows where he may not
            float sr = AirportSpec.ServiceRoadWidth * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                float z = AirportSpec.ServiceRoadZ + s * sr;
                white.Dashes(new Vector3(AirportSpec.ApronX0 - 24f, 0f, z), new Vector3(AirportSpec.ApronX1 + 24f, 0f, z), 0.2f, 3f, 3f, y);
            }

            // the apron edge line: where the concrete stops being anybody's to park on
            yellow.Rect(AirportSpec.ApronX0, AirportSpec.ApronX1, AirportSpec.ApronZ0, AirportSpec.ApronZ0 + 0.2f, y);

            yellow.Emit("Ramp markings", _yellowPaint, _markingRoot);
            white.Emit("Ramp markings white", _whitePaint, _markingRoot);
        }

        // ------------------------------------------------------------ the lights

        void BuildAirfieldLights()
        {
            if (!airfieldLighting) return;
            float half = RunwayHalf;
            float edge = AirportSpec.RunwayHalfWidth + 2.5f;
            int laid = 0;

            // runway edge lights every 60 m, amber over the last 600 m of each end -
            // what a pilot sees as the runway shortening ahead of him
            for (float x = -half; x <= half + 0.1f; x += 60f)
            {
                bool amber = x > half - 600f || x < -half + 600f;
                var prefab = amber ? _lightAmber : _lightWhite;
                for (int s = -1; s <= 1; s += 2)
                    if (Light(prefab, new Vector3(x, AirportSpec.LightY, s * edge))) laid++;
            }

            // the threshold: green out to the approach, red back down the runway
            for (int end = 0; end < 2; end++)
            {
                float sign = end == 0 ? -1f : 1f;
                for (int i = 0; i < 8; i++)
                {
                    float z = (i - 3.5f) / 3.5f * (AirportSpec.RunwayHalfWidth - 1f);
                    if (Light(_lightGreen, new Vector3(sign * (half + 1.5f), AirportSpec.LightY, z))) laid++;
                    if (Light(_lightRed, new Vector3(sign * (half - 1.5f), AirportSpec.LightY, z))) laid++;
                }
            }

            // taxiway edge lights, blue, and the connectors' too
            float tz = AirportSpec.TaxiwayZ, te = AirportSpec.TaxiwayHalf + 2f;
            for (float x = -half; x <= half + 0.1f; x += 60f)
                for (int s = -1; s <= 1; s += 2)
                    if (Light(_lightBlue, new Vector3(x, AirportSpec.LightY, tz + s * te))) laid++;
            foreach (float cx in AirportSpec.ConnectorX)
            {
                float x = Mathf.Clamp(cx, -half + 30f, half - 30f);
                for (float z = AirportSpec.RunwayHalfWidth + 10f; z < tz - 10f; z += 25f)
                    for (int s = -1; s <= 1; s += 2)
                        if (Light(_lightBlue, new Vector3(x + s * te, AirportSpec.LightY, z))) laid++;
            }

            // PAPI, one to each end, on the left of the approach as the pilot sees it
            var papi = AirportKit.TryLoad(AirportKit.Papi);
            if (papi != null)
            {
                AirportKit.Prop(papi, new Vector3(-half + AirportSpec.PapiFromThreshold, AirportSpec.PaveY, AirportSpec.PapiZ), 0f, _lightRoot, "PAPI 09");
                AirportKit.Prop(papi, new Vector3(half - AirportSpec.PapiFromThreshold, AirportSpec.PaveY, -AirportSpec.PapiZ), 180f, _lightRoot, "PAPI 27");
            }

            // the apron floodlight masts, which is how a ramp is lit
            var mast = AirportKit.TryLoad(AirportKit.ApronMast);
            if (mast != null)
                for (float x = AirportSpec.ApronX0 + 60f; x < AirportSpec.ApronX1; x += 110f)
                    AirportKit.Prop(mast, new Vector3(x, AirportSpec.PaveY, AirportSpec.ApronZ1 - 3f), 180f, _lightRoot, "Apron mast");

            Debug.Log($"[AirportDemo] {laid} airfield lights round the runway and the taxiway");
        }

        bool Light(GameObject prefab, Vector3 at)
        {
            if (prefab == null) return false;
            AirportKit.Prop(prefab, at, 0f, _lightRoot, "light");
            return true;
        }

        // ------------------------------------------------------------ the windsock

        /// <summary>The windsock in its segmented circle, and the two guidance boards
        /// at every connector - the location board with the taxiway's letter, and the
        /// red board with the runway's designation that says stop.</summary>
        void BuildWindsock()
        {
            var sock = AirportKit.TryLoad(AirportKit.Windsock);
            // the sock streams downwind: with a westerly, it points east
            float sockYaw = westerlyWind ? 90f : 270f;
            if (sock != null)
            {
                var go = AirportKit.Prop(sock, new Vector3(AirportSpec.WindsockX, AirportSpec.LandY, AirportSpec.WindsockZ),
                                         sockYaw, _airsideRoot, "Windsock");
                _windsocks.Add(go.transform);
            }

            // the segmented circle: white boards laid in a ring round the sock, which
            // is what tells a pilot with no radio which way the circuit goes
            var circle = new Painter();
            int segments = 12;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var p = new Vector3(AirportSpec.WindsockX + Mathf.Cos(a) * AirportSpec.SegmentedCircleRadius,
                                    0f,
                                    AirportSpec.WindsockZ + Mathf.Sin(a) * AirportSpec.SegmentedCircleRadius);
                circle.Turned(p, -a * Mathf.Rad2Deg, 1.4f, 4.2f, AirportSpec.LandY + 0.03f);
            }
            circle.Emit("Segmented circle", _whitePaint, _markingRoot);

            // the boards at the holding positions
            var taxiSign = AirportKit.TryLoad(AirportKit.TaxiSign);
            var holdSign = AirportKit.TryLoad(AirportKit.HoldSign);
            float half = RunwayHalf, th = AirportSpec.TaxiwayHalf;
            for (int i = 0; i < AirportSpec.ConnectorX.Length; i++)
            {
                float x = Mathf.Clamp(AirportSpec.ConnectorX[i], -half + 30f, half - 30f);
                float hz = AirportSpec.HoldShortZ;
                if (holdSign != null)
                {
                    AirportKit.Prop(holdSign, new Vector3(x - th - 3.5f, AirportSpec.PaveY, hz + 1.5f), 180f, _airsideRoot, "Hold sign");
                    AirportKit.Prop(holdSign, new Vector3(x + th + 3.5f, AirportSpec.PaveY, hz + 1.5f), 180f, _airsideRoot, "Hold sign");
                }
                if (taxiSign != null)
                    AirportKit.Prop(taxiSign, new Vector3(x + th + 3.5f, AirportSpec.PaveY, AirportSpec.TaxiwayZ + th + 3.5f), 180f, _airsideRoot, "Taxi sign");
            }

            // the letter on each board, painted the way the runway numbers are
            var legend = new Painter();
            for (int i = 0; i < AirportSpec.ConnectorX.Length; i++)
            {
                float x = Mathf.Clamp(AirportSpec.ConnectorX[i], -half + 30f, half - 30f);
                // the taxiway's own designation, painted on the pavement beside the sign
                PaintLegend(legend, AirportSpec.ConnectorName[i], new Vector3(x + AirportSpec.TaxiwayHalf + 8f, 0f, AirportSpec.TaxiwayZ),
                            3.5f, 180f, AirportSpec.MarkY, tightGap: true);
            }
            legend.Emit("Taxiway designations", _yellowPaint, _markingRoot);
        }
    }
}
