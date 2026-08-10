using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrafficLightsControl : MonoBehaviour
{
    public TrafficLight[] firstLights;
    public TrafficLight[] secondLights;
    public float timeInterval = 10;

    // PATCH (Living City): an all-red clearance interval between phases.
    //
    // The pack flipped one direction red and the other green in the SAME call, so a car still
    // inside the box met cross traffic that was already accelerating. Every junction in the city
    // is now a four-lane crossroads, which is 12.5m of asphalt to clear rather than 6, so the
    // window a late car needs got wider at the same time.
    //
    // 1.5s is what a car doing the 45km/h cap needs to cover the ~19m from the stop line to the
    // far kerb. Set to 0 to restore the pack's behaviour.
    public float allRedInterval = 1.5f;

    private bool firstOn = false;

    void Start()
    {
        StartCoroutine(Cycle());
    }

    // PATCH (Living City): was InvokeRepeating("ToggleLights", 0, timeInterval). A coroutine
    // because the cycle now has two unequal phases rather than one repeating beat.
    private IEnumerator Cycle()
    {
        while (true)
        {
            ToggleLights();
            yield return new WaitForSeconds(timeInterval);

            if (allRedInterval > 0f)
            {
                AllRed();
                yield return new WaitForSeconds(allRedInterval);
            }
        }
    }

    private void ToggleLights()
    {
        //Changes collors
        Color firstColor;
        Color secondColor;
        if (firstOn)
        {
            firstColor = Color.red;
            secondColor = Color.green;
            firstOn = false;
        } else
        {
            firstColor = Color.green;
            secondColor = Color.red;
            firstOn = true;
        }

        Apply(firstLights, firstColor, firstOn, notify: true);
        Apply(secondLights, secondColor, !firstOn, notify: true);
    }

    /// <summary>
    /// PATCH (Living City): every light red, and deliberately WITHOUT firing lightChange.
    ///
    /// Nothing needs the event here and two things break on it. TrafficLight.ChangeCrosswalk
    /// toggles crosswalk.CanCross rather than assigning it, so an extra invoke permanently
    /// inverts pedestrian permission at that junction - the desync CarBehavior's HoldWatchdog
    /// exists to survive. And a car already stopped at the line is holding a reference to its own
    /// light; leaving it uninvoked keeps it held, which is exactly right, while a car arriving
    /// during the clearance reads isGreen on trigger entry and stops of its own accord.
    ///
    /// Both are released by their own light's lightChange when the next phase turns it green -
    /// CarBehavior.StartMoving already ignores an invoke that carries isGreen false, and the
    /// delegate is invoked per light with that light's own state.
    /// </summary>
    private void AllRed()
    {
        Apply(firstLights, Color.red, false, notify: false);
        Apply(secondLights, Color.red, false, notify: false);
    }

    private static void Apply(TrafficLight[] lights, Color color, bool green, bool notify)
    {
        if (lights == null)
            return;

        foreach (TrafficLight light in lights)
        {
            if (!light)
                continue;

            light.GetComponent<Renderer>().materials[1].SetColor("_EmissionColor", color);
            light.isGreen = green;

            if (notify)
                light.lightChange?.Invoke(light.isGreen);
        }
    }
}
