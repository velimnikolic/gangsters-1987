using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using RoadDemo;

public static class ResidentialNeglectAudit
{
    public static object Main()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/ResidentialDemo.unity");
        try
        {
            var failures = new List<string>();
            var roots = scene.GetRootGameObjects();
            var group = roots.FirstOrDefault(g => g.name == "RESIDENTIAL NEGLECTED COMPARISON");
            if (!group) throw new InvalidOperationException("Comparison set missing.");
            var originals = roots.Where(g => g.name.StartsWith("RESIDENTIAL ") && g != group).ToArray();
            var expected = originals.Where(g => !g.name.ToLowerInvariant().Contains("police") &&
                !g.name.ToLowerInvariant().Contains("nightclub")).ToArray();
            var actual = group.transform.Cast<Transform>().Select(t => t.gameObject).ToArray();
            if (!expected.Select(g => g.name).OrderBy(n => n).SequenceEqual(actual.Select(g => g.name).OrderBy(n => n)))
                failures.Add("Block names/count differ.");
            int boards = 0, tags = 0, litter = 0;
            foreach (var block in actual)
            {
                var original = expected.FirstOrDefault(g => g.name == block.name);
                if (!original) continue;
                if (Vector3.Distance(original.transform.position + group.transform.position, block.transform.position) > .001f)
                    failures.Add("Mismatched relative position: " + block.name);
                var dressing = block.transform.Find("Neglected district dressing");
                if (!dressing) { failures.Add("No dressing: " + block.name); continue; }
                foreach (var collider in dressing.GetComponentsInChildren<Collider>())
                    if (collider.enabled) failures.Add("Blocking new collider.");
                foreach (var t in dressing.GetComponentsInChildren<Transform>())
                {
                    if (t.name == "Boarded upper window")
                    {
                        boards++;
                        if (t.position.y < block.transform.position.y + 3) failures.Add("Ground-level boarding.");
                    }
                    if (t.name == "Faded wall tag") tags++;
                    if (t.name.StartsWith("Overflow litter")) litter++;
                }
                int before = original.GetComponentsInChildren<MeshFilter>(true).Length;
                int after = block.GetComponentsInChildren<MeshFilter>(true).Count(f => !f.transform.IsChildOf(dressing));
                if (before != after) failures.Add("Original geometry count changed: " + block.name);
                foreach (var mf in block.GetComponentsInChildren<MeshFilter>(true))
                    if (!mf.sharedMesh) failures.Add("Missing mesh.");
                foreach (var mr in block.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var material in mr.sharedMaterials)
                        if (!material || !material.shader || !material.shader.isSupported) failures.Add("Missing/unsupported material.");
            }
            float normalMax = originals.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).Max(r => r.bounds.max.x);
            float neglectedMin = group.GetComponentsInChildren<MeshRenderer>(true).Min(r => r.bounds.min.x);
            if (neglectedMin - normalMax < 44.9f) failures.Add("Sets overlap or gap is too narrow.");
            var camera = roots.SelectMany(g => g.GetComponentsInChildren<Camera>()).First();
            var rig = camera.GetComponent<DemoCamera>();
            var comparison = camera.GetComponent<ResidentialComparisonView>();
            if (!rig || rig.mapTransition || !comparison || comparison.rig != rig || comparison.offset != group.transform.position)
                failures.Add("Comparison controls missing or mismatched.");
            var errors = ShaderUtil.GetShaderMessages(Shader.Find("LivingCity/Residential Neglect"));
            failures.AddRange(errors.Select(e => e.message));
            return new { passed = failures.Count == 0, normalBlocks = originals.Length,
                neglectedBlocks = actual.Length, gap = neglectedMin - normalMax, boards, tags, litter, failures };
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
