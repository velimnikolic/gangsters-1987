using System;
using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

static class Program
{
    static CrewOverlay overlay;
    static DemoCamera camera;
    static bool useOverlay;
    static int failed, executed;

    static int Main()
    {
        Check("ordinary click reaches dispatch once", () => {
            Frame(press: true, held: true); Frame(release: true);
            Require(overlay.Orders == 1 && RightPointerGesture.Clicked);
            Frame(); Require(overlay.Orders == 1 && !RightPointerGesture.Clicked);
        });
        Check("press and release in one frame reach dispatch", () => {
            Frame(press: true, release: true);
            Require(overlay.Orders == 1 && RightPointerGesture.Clicked);
        });
        Check("stationary long hold remains a click", () => {
            Frame(press: true, held: true);
            for (int i = 0; i < 120; i++) Frame(held: true);
            Frame(release: true); Require(overlay.Orders == 1);
        });
        Check("hand jitter never starts camera drag", () => {
            Frame(press: true, held: true);
            Frame(x: 4, held: true); Require(!RightPointerGesture.Dragging && camera.yaw == 0);
            Frame(x: 4, release: true); Require(overlay.Orders == 1);
        });
        Check("drag returning to origin cannot order", () => {
            Frame(press: true, held: true);
            Frame(x: 20, held: true); Require(RightPointerGesture.Dragging);
            Frame(x: 0, held: true); Require(RightPointerGesture.Dragging);
            Frame(release: true); Require(overlay.Orders == 0 && !RightPointerGesture.Dragging);
        });
        Check("release-frame movement counts as drag", () => {
            Frame(press: true, held: true); Frame(x: 20, release: true);
            Require(overlay.Orders == 0 && !RightPointerGesture.Clicked);
        });
        Check("UI press dragged into street stays claimed", () => {
            EventSystem.current.Over = true; Frame(press: true, held: true);
            EventSystem.current.Over = false; Frame(x: 30, held: true);
            Require(!RightPointerGesture.Dragging);
            Frame(release: true); Require(overlay.Orders == 0);
        });
        Check("release over UI cannot order behind it", () => {
            Frame(press: true, held: true); EventSystem.current.Over = true;
            Frame(release: true); Require(overlay.Orders == 0);
        });
        Check("modal opened and closed during press cancels order", () => {
            Frame(press: true, held: true); LivingCity.UI.ModalGate.Any = true;
            Frame(held: true); LivingCity.UI.ModalGate.Any = false;
            Frame(release: true); Require(overlay.Orders == 0);
        });
        Check("crew bar claims the whole gesture", () => {
            CrewBar.Instance = new CrewBar(); Frame(press: true, held: true);
            CrewBar.Instance = null; Frame(release: true); Require(overlay.Orders == 0);
        });
        Check("focus loss cancels pending click", () => {
            Frame(press: true, held: true); Application.isFocused = false;
            Frame(held: true); Application.isFocused = true;
            Frame(release: true); Require(overlay.Orders == 0);
        });
        Check("lost device cannot leave pending click", () => {
            Frame(press: true, held: true); Mouse.current = null;
            Time.frameCount++; Require(!RightPointerGesture.Clicked && !RightPointerGesture.Dragging);
            Frame(release: true); Require(overlay.Orders == 0);
        });
        Check("missed release cancels pending click", () => {
            Frame(press: true, held: true); Frame(); Frame(release: true);
            Require(overlay.Orders == 0);
        });
        Check("stray release is harmless", () => {
            Frame(release: true); Require(overlay.Orders == 0 && !RightPointerGesture.Clicked);
        });
        Check("high resolution uses scaled drag threshold", () => {
            Screen.height = 2160; Frame(press: true, held: true);
            Frame(x: 12, held: true); Require(!RightPointerGesture.Dragging);
            Frame(x: 12, release: true); Require(overlay.Orders == 1);
        });
        Check("small window keeps usable jitter tolerance", () => {
            Screen.height = 540; Frame(press: true, held: true);
            Frame(x: 6, held: true); Require(!RightPointerGesture.Dragging);
            Frame(x: 6, release: true); Require(overlay.Orders == 1);
        });
        Check("closing order card does not dispatch", () => {
            overlay._ordersOpen = true; Frame(press: true, release: true);
            Require(!overlay._ordersOpen && overlay.Orders == 0);
            Frame(press: true, release: true); Require(overlay.Orders == 1);
        });
        Check("same-frame cover click orders only cover", () => {
            overlay.Cover = true; Frame(press: true, release: true);
            Require(overlay.CoverOrders == 1 && overlay.Orders == 0 && !overlay._aiming);
        });
        Check("held cover aim keeps its own release", () => {
            overlay.Cover = true; Frame(press: true, held: true);
            Frame(x: 40, held: true); Frame(x: 40, release: true);
            Require(overlay.CoverOrders == 1 && overlay.Orders == 0);
        });
        Check("cover release over UI cannot order", () => {
            overlay.Cover = true; Frame(press: true, held: true);
            EventSystem.current.Over = true; Frame(release: true);
            Require(overlay.CoverOrders == 0 && overlay.Orders == 0);
        });
        Check("orbit continues across HUD and back", () => {
            Frame(press: true, held: true); Frame(x: 20, held: true);
            float before = camera.yaw; EventSystem.current.Over = true;
            Frame(x: 30, held: true); Require(camera.yaw > before);
            before = camera.yaw; EventSystem.current.Over = false;
            Frame(x: 40, held: true); Require(camera.yaw > before);
            Frame(release: true); Require(overlay.Orders == 0);
        });
        Check("camera alone samples press behind condition controls", () => {
            useOverlay = false; CityConditionHud.PointerOverControls = true;
            Frame(press: true, held: true); Require(camera.yaw == 0);
            CityConditionHud.PointerOverControls = false;
            Frame(x: 20, held: true); Require(camera.yaw > 0);
        });
        Check("camera alone samples release while suppressed", () => {
            useOverlay = false; Frame(press: true, held: true); Frame(x: 20, held: true);
            camera.SuppressInput = true; Frame(release: true);
            camera.SuppressInput = false; Frame(x: 30);
            Require(!RightPointerGesture.Dragging && !RightPointerGesture.Clicked);
        });
        Check("camera alone clears disconnected device", () => {
            useOverlay = false; Frame(press: true, held: true);
            Mouse.current = null; Time.frameCount++; camera.Poll();
            Frame(release: true); Require(!RightPointerGesture.Clicked);
        });
        Check("cover aim lost release clears preview and camera ownership", () => {
            overlay.Cover = true; Frame(press: true, held: true);
            Require(overlay._aiming && DemoCamera.RightDragTaken);
            Frame(); Require(!overlay._aiming && !DemoCamera.RightDragTaken && overlay.CoverOrders == 0);
        });
        Check("cover aim device loss clears preview and camera ownership", () => {
            overlay.Cover = true; Frame(press: true, held: true);
            Mouse.current = null; Time.frameCount++; camera.Poll(); overlay.TickAim();
            Require(!overlay._aiming && !DemoCamera.RightDragTaken && overlay.CoverOrders == 0);
        });
        Check("cover aim focus loss cancels before release", () => {
            overlay.Cover = true; Frame(press: true, held: true);
            Application.isFocused = false; Frame(held: true);
            Require(!overlay._aiming && overlay.CoverOrders == 0);
            Application.isFocused = true; Frame(release: true);
            Require(!DemoCamera.RightDragTaken && overlay.CoverOrders == 0);
        });
        Check("release then press after orbit keeps the new click", () => {
            Frame(press: true, held: true); Frame(x: 20, held: true);
            Frame(x: 30, press: true, release: true, held: true);
            Require(overlay.Orders == 0 && !RightPointerGesture.Clicked);
            Frame(x: 30, release: true); Require(overlay.Orders == 1);
        });
        Check("release then press after orbit keeps the new drag", () => {
            Frame(press: true, held: true); Frame(x: 20, held: true);
            Frame(x: 30, press: true, release: true, held: true);
            float before = camera.yaw;
            Frame(x: 50, held: true); Require(camera.yaw > before);
            Frame(x: 50, release: true); Require(overlay.Orders == 0);
        });
        Check("release then press retires old cover ownership", () => {
            overlay.Cover = true; Frame(press: true, held: true);
            overlay.Cover = false; Frame(press: true, release: true, held: true);
            Require(!overlay._aiming && !DemoCamera.RightDragTaken && overlay.CoverOrders == 0);
            Frame(x: 20, held: true); Require(camera.yaw > 0);
            Frame(x: 20, release: true); Require(overlay.Orders == 0 && overlay.CoverOrders == 0);
        });
        Check("cover sweep crosses ordinary HUD and returns before release", () => {
            overlay.Cover = true; Frame(press: true, held: true);
            EventSystem.current.Over = true; Frame(x: 40, held: true);
            Require(overlay._aiming && DemoCamera.RightDragTaken && overlay.CoverOrders == 0);
            EventSystem.current.Over = false; Frame(x: 80, held: true);
            Frame(x: 80, release: true);
            Require(!overlay._aiming && !DemoCamera.RightDragTaken &&
                    overlay.CoverOrders == 1 && overlay.Orders == 0);
        });
        Console.WriteLine($"{executed - failed}/{executed} PASS: scripted input/admission scenarios; no Unity/Play");
        return failed == 0 ? 0 : 1;
    }

    static void Frame(float x = 0, bool press = false, bool release = false, bool held = false)
    {
        Time.frameCount++; Time.unscaledTime += 1f / 60f;
        Mouse.current ??= new Mouse();
        Mouse.current.delta.Value = new Vector2(x - Mouse.current.position.Value.x, 0);
        Mouse.current.position.Value = new Vector2(x, 0);
        Mouse.current.rightButton.wasPressedThisFrame = press;
        Mouse.current.rightButton.wasReleasedThisFrame = release;
        Mouse.current.rightButton.isPressed = held;
        // Execute the production camera sampling/gating block, including frames
        // with no overlay or with input suppressed by a condition control.
        camera.Poll();
        if (useOverlay) { overlay.Poll(); overlay.TickAim(); }
    }

    static void Check(string name, Action test)
    {
        executed++;
        overlay = new CrewOverlay();
        camera = new DemoCamera(); useOverlay = true;
        DemoCamera.RightDragTaken = CityConditionHud.PointerOverControls = false;
        EventSystem.current.Over = LivingCity.UI.ModalGate.Any = false;
        CrewBar.Instance = null; Screen.height = 1080; Application.isFocused = true;
        Mouse.current = null; Time.frameCount++;
        _ = RightPointerGesture.Clicked;
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }

    static void Require(bool value)
    { if (!value) throw new Exception("contract failed"); }
}
