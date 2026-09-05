using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using RoadDemo;
public static class ResidentialNeglectAudit {
 public static object Main(){
 var src=EditorSceneManager.OpenPreviewScene("Assets/Scenes/ResidentialDemo.unity");
 var dst=EditorSceneManager.OpenPreviewScene("Assets/Scenes/NeglectedResidentialDemo.unity");
 try {
 var failures=new List<string>();
 var originals=src.GetRootGameObjects().Where(g=>g.name.StartsWith("RESIDENTIAL ")).ToArray();
 var expected=originals.Where(g=>!g.name.ToLowerInvariant().Contains("police")&&!g.name.ToLowerInvariant().Contains("nightclub")).ToArray();
 var actual=dst.GetRootGameObjects().Where(g=>g.name.StartsWith("RESIDENTIAL ")).ToArray();
 if(expected.Length!=actual.Length)failures.Add("Wrong block count");
 int boards=0,tags=0,litter=0;
 foreach(var block in actual){
 var original=expected.FirstOrDefault(g=>g.name==block.name);
 if(!original){failures.Add("Unexpected block "+block.name);continue;}
 if(original.transform.position!=block.transform.position)failures.Add("Moved "+block.name);
 var dressing=block.transform.Find("Neglected district dressing");
 if(!dressing){failures.Add("No dressing "+block.name);continue;}
 foreach(var c in dressing.GetComponentsInChildren<Collider>())if(c.enabled)failures.Add("Blocking new collider");
 foreach(var t in dressing.GetComponentsInChildren<Transform>()){
 if(t.name=="Boarded upper window"){boards++;if(t.position.y<3)failures.Add("Ground-level boarding");}
 if(t.name=="Faded wall tag")tags++;
 if(t.name.StartsWith("Overflow litter"))litter++;
 }
 int before=original.GetComponentsInChildren<MeshFilter>(true).Length;
 int after=block.GetComponentsInChildren<MeshFilter>(true).Count(f=>!f.transform.IsChildOf(dressing));
 if(before!=after)failures.Add("Original geometry changed "+block.name);
 foreach(var mf in block.GetComponentsInChildren<MeshFilter>(true))if(!mf.sharedMesh)failures.Add("Missing mesh");
 foreach(var mr in block.GetComponentsInChildren<MeshRenderer>(true))foreach(var m in mr.sharedMaterials)if(!m||!m.shader||!m.shader.isSupported)failures.Add("Missing/unsupported material");
 }
 var camera=dst.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>()).First();
 var rig=camera.GetComponent<DemoCamera>();if(!rig||rig.mapTransition||!rig.showHint)failures.Add("Camera controls/hint absent");
 var errors=ShaderUtil.GetShaderMessages(Shader.Find("LivingCity/Residential Neglect"));
 if(errors.Length>0)failures.AddRange(errors.Select(e=>e.message));
 return new {passed=failures.Count==0,sourceBlocks=originals.Length,expected=expected.Length,actual=actual.Length,boards,tags,litter,failures};
 }finally{EditorSceneManager.ClosePreviewScene(dst);EditorSceneManager.ClosePreviewScene(src);}
 }
}
