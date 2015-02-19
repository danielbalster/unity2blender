/*
 * Unity plugin to export to Blender/Beerengine
 *
 * Place in "Editor" folder and choose "File / Export to Blender"
 *
 * Copyright (C) Daniel Balster
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of Daniel Balster nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY DANIEL BALSTER ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL DANIEL BALSTER BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

public static class Extensions
{
	public static string ID (this UnityEngine.Object self)
	{
		return self.name + "#" + self.GetInstanceID ();
	}
}

public class BlenderExporter
{
	TextWriter script;
	
	public BlenderExporter ()
	{
	}
	
	void Recurse (Transform trfm, Action<Transform> action)
	{
		action (trfm);
		foreach (Transform child in trfm) {
			Recurse (child, action);
		}
	}
	
	void Recurse (Action<Transform> action)
	{
		foreach (Transform trfm in UnityEngine.Object.FindObjectsOfType(typeof(Transform))) {
			if (trfm.parent == null) {
				Recurse (trfm, action);
			}
		}
	}
	
	bool ExportLight (Light go)
	{
		string id = go.ID ();
		
		if (go.type == LightType.Area)
			return false;
		
		if (go.type == LightType.Point) {
			WriteLine ("l=bpy.data.lamps.new(name='{0}',type='POINT')", id);
		} else if (go.type == LightType.Spot) {
			WriteLine ("l=bpy.data.lamps.new(name='{0}',type='SPOT')", id);
		}
		if (go.type == LightType.Directional) {
			WriteLine ("l=bpy.data.lamps.new(name='{0}',type='HEMI')", id);
		}
		
		WriteLine ("l.energy={0}", go.intensity);
		WriteLine ("l.distance={0}", go.range);
		//WriteLine ("l.clip_end=4");
		WriteLine ("l.color={0},{1},{2}", go.color.r, go.color.g, go.color.b);
		if (go.type == LightType.Spot) {
			WriteLine ("l.spot_size={0}", go.spotAngle * 3.141596 / 180);
		}
		
		return true;
	}
	
	HashSet<string> meshes = new HashSet<string> ();
	
	public void ExportMesh (Mesh amesh, Mesh mesh, Material mat)
	{
		int csum = 0;
		
		// expensive and lame
		if (mesh.vertexCount > 0) {
			for (int i = 0, n = mesh.vertexCount; i < n; ++i) {
				var v = mesh.vertices [i];
				var x = BitConverter.GetBytes (v.x);
				var y = BitConverter.GetBytes (v.y);
				var z = BitConverter.GetBytes (v.z);
				for (int j=0; j<4; ++j)
				{
					csum = unchecked(csum + x[j]);
					csum = unchecked(csum + y[j]);
					csum = unchecked(csum + z[j]);
				}
			}
		}
		
		var id = mesh.name + "#" + csum;
		if (meshes.Contains (id)) {
			WriteLine ("me=Me['{0}']", id);
			return;
		}
		meshes.Add (id);
		// Positions
		WriteLine ("me=Me.new('{0}')", id);
		var verts = new StringBuilder ();
		var faces = new StringBuilder ();
		if (mesh.vertexCount > 0) {
			for (int i = 0, n = mesh.vertexCount; i < n; ++i) {
				var v = mesh.vertices [i];
				verts.AppendFormat ("({0},{1},{2}),", v.x, v.y, v.z);
			}
		}
		if (mesh.uv.Length > 0) {
			Write ("uv1=[");
			for (int i = 0, n = mesh.uv.Length; i < n; ++i) {
				var uv = mesh.uv [i];
				Write ("({0},{1}),", uv.x, uv.y);
			}
			WriteLine ("]");
		}
		/*
		bool nmap = mat.GetTexture ("_BumpMap") != null;
		if (nmap) {
			if (mesh.uv2.Length > 0) {
				Write ("uv2=[");
				for (int i = 0, n = mesh.uv2.Length; i < n; ++i) {
					var uv = mesh.uv2 [i];
					Write ("({0},{1}),", uv.x, uv.y);
				}
				WriteLine ("]");
			}
		}
		*/
		int[] tris = mesh.triangles;
		for (int i = 0, n = tris.Length; i < n; i+=3) {
			faces.AppendFormat ("({0},{1},{2}),", tris [i], tris [i + 1], tris [i + 2]);
		}
		WriteLine ("me.from_pydata([{0}],[],[{1}])", verts.ToString (), faces.ToString ());
		if (mesh.uv != null && mesh.uv.Length > 0) {
			WriteLine ("uvt=me.uv_textures.new(name='UVMap')");
			WriteLine ("uvl=me.uv_layers[0].data");
			WriteLine ("for p in me.polygons:");
			WriteLine ("\tfor l in range(p.loop_start,p.loop_start+p.loop_total):");
			WriteLine ("\t\tuvl[l].uv=uv1[me.loops[l].vertex_index]");
		}
		/*		if (mesh.uv2 != null && mesh.uv2.Length > 0) {
				WriteLine ("uvt=me.uv_textures.new(name='UVMap')");
				WriteLine ("uvl=me.uv_layers[1].data");
				WriteLine ("for p in me.polygons:");
				WriteLine ("\tfor l in range(p.loop_start,p.loop_start+p.loop_total):");
				WriteLine ("\t\tuvl[l].uv=uv2[me.loops[l].vertex_index]");
			}
*/
		
	}
	
	HashSet<string> images = new HashSet<string> ();
	HashSet<string> textures = new HashSet<string> ();
	
	public bool ExportTexture (Texture tex)
	{
		if (tex == null)
			return false;
		
		var id = tex.ID ();
		
		if (textures.Contains (id)) {
			WriteLine ("t=T['{0}']", id);
			return true;
		}
		textures.Add (id);
		
		WriteLine ("t=T.new('{0}',type='IMAGE')", id);
		
		var path = UnityEditor.AssetDatabase.GetAssetOrScenePath (tex);
		if (path.Length != 0) {
			path = System.IO.Path.GetFullPath (path);
			
			if (!images.Contains (path)) {
				images.Add (path);
				
				//var basename = System.IO.Path.GetFileNameWithoutExtension (path);
				//var filename = System.IO.Path.GetFileName (path);
				
				WriteLine ("i=I.load('{0}')", path);
				WriteLine ("i.pack(as_png=True)");
				WriteLine ("i.filepath=os.path.splitext(i.filepath)[0]+'.png'");
			} else {
				WriteLine ("i=I['{0}']", path);
			}
			WriteLine ("t.image=i");
		}
		
		return true;
	}
	
	HashSet<string> materials = new HashSet<string> ();
	
	void ExportMaterial (Material mat)
	{
		if (materials.Contains (mat.ID ())) {
			WriteLine ("ma=Ma['{0}']", mat.ID ());
			return;
		}
		materials.Add (mat.ID ());
		WriteLine ("ma=Ma.new('{0}')", mat.ID ());
		
		WriteLine ("ma.diffuse_color={0},{1},{2}", mat.color.r, mat.color.g, mat.color.b);
		
		bool additive = mat.shader.name.ToLower().Contains ("additive");
		bool cutout = mat.shader.name.ToLower().Contains ("cutout");
		
		if (cutout) {
			WriteLine ("ma.use_transparency=True");
			WriteLine ("ma.game_settings.alpha_blend='CLIP'");
		}
		if (additive) {
			WriteLine ("ma.use_transparency=True");
			WriteLine ("ma.game_settings.alpha_blend='ADD'");
			WriteLine("ma.beerengine_cull_enable=False");
			WriteLine("ma.beerengine_blend_func_src='GL_ONE'");
			WriteLine("ma.beerengine_blend_func_dst='GL_ONE'");
			WriteLine("ma.beerengine_blend_func_src_a='GL_ONE'");
			WriteLine("ma.beerengine_blend_func_dst_a='GL_ONE'");
		}
		
		if (ExportTexture (mat.GetTexture ("_MainTex"))) {
			WriteLine ("ts=ma.texture_slots.add() # maintex");
			WriteLine ("ts.texture=t");
			WriteLine ("ts.texture_coords='UV'");
			WriteLine ("ts.uv_layer='UVMap'");
			WriteLine ("ts.use_map_color_diffuse=True");
		}
		
		if (ExportTexture (mat.GetTexture ("_BumpMap"))) {
			WriteLine ("ts=ma.texture_slots.add() # bumpmap");
			WriteLine ("ts.texture=t");
			WriteLine ("ts.texture_coords='UV'");
			WriteLine ("ts.uv_layer='UVMap'");
			WriteLine ("ts.use_map_color_diffuse=False");
			WriteLine ("ts.use_map_normal=True");
		}
		
		//	if (ExportTexture (mat.GetTexture ("_Cube"))) {
		//		WriteLine("ts=ma.texture_slots.add() # cubemap");
		//		WriteLine("ts.texture=t");
		//	}
		
		/*
		if (ExportTexture (mat.GetTexture ("_LightmapTex"))) {
			WriteLine("ts=ma.texture_slots.add() # lightmap");
			WriteLine("ts.texture=t");
			WriteLine("ts.texture_coords='UV'");
			WriteLine("ts.uv_layer='LM'");
			WriteLine("ts.use_map_color_diffuse=True");
		}
*/
	}
	
	public void Export ()
	{
		script = File.CreateText ("export.py");
		WriteLine ("#!/usr/bin/python");
		WriteLine ("import bpy");
		WriteLine ("import os");
		WriteLine ("S=bpy.context.scene.objects");
		WriteLine ("D=bpy.data");
		WriteLine ("Me=D.meshes");
		WriteLine ("Ma=D.materials");
		WriteLine ("O=D.objects");
		WriteLine ("T=D.textures");
		WriteLine ("I=D.images");
		WriteLine ("Q='QUATERNION'");
		
		var meshedobjects = new HashSet<string> ();
		
		Recurse ((trfm) => {
			
			//if (trfm.parent != null)
			//	return;
			
			var name = "None";
			
			var mf = trfm.GetComponent<MeshFilter> ();
			if (mf != null && mf.sharedMesh != null && trfm.light == null) {
				Material mat = null;
				if (trfm.renderer != null) {
					mat = trfm.renderer.sharedMaterial;
				}
				
				ExportMesh (mf.mesh,mf.sharedMesh, mat);
				name = "me";
				meshedobjects.Add (trfm.ID());
			}
			
			if (trfm.light != null) {
				ExportLight (trfm.light);
				name = "l";
			}
			
			WriteLine ("o=O.new('{0}',{1})", trfm.ID (), name);
			
			if (name=="None")
			{
				WriteLine ("o.empty_draw_type='SINGLE_ARROW'");
			}
			
			//WriteLine("o.show_name = True");
			if (trfm.parent != null) {
				if (trfm.light != null) {
					// flip lights around x-axis
					var m = Matrix4x4.TRS (trfm.localPosition, trfm.localRotation, trfm.localScale);
					m = m * Matrix4x4.TRS (Vector3.zero, Quaternion.Euler (-180, 0, 0), Vector3.one);
					WriteLine ("o.matrix_local=(({0},{1},{2},{3}),({4},{5},{6},{7}),({8},{9},{10},{11}),({12},{13},{14},{15}))",
					           m [0], m [1], m [2], m [3],
					           m [4], m [5], m [6], m [7],
					           m [8], m [9], m [10], m [11],
					           m [12], m [13], m [14], m [15]
					           );
				} else {
					WriteLine ("o.location=({0},{1},{2})", trfm.localPosition.x, trfm.localPosition.y, trfm.localPosition.z);
					WriteLine ("o.scale=({0},{1},{2})", trfm.localScale.x, trfm.localScale.y, trfm.localScale.z);
					WriteLine ("o.rotation_mode=Q");
					WriteLine ("o.rotation_quaternion=[{0},{1},{2},{3}]", trfm.localRotation.w, trfm.localRotation.x, trfm.localRotation.y, trfm.localRotation.z);
				}
				WriteLine ("if O['{0}'] is not None:", trfm.parent.ID ());
				WriteLine ("\to.parent=O['{0}']", trfm.parent.ID ());
			} else {
				// rotate root nodes 90° on x-axis (unity -> blender coordinate system)
				var m = Matrix4x4.TRS (trfm.localPosition, trfm.localRotation, trfm.localScale);
				m = Matrix4x4.TRS (Vector3.zero, Quaternion.Euler (90, 0, 0), Vector3.one) * m;
				WriteLine ("o.matrix_local=(({0},{1},{2},{3}),({4},{5},{6},{7}),({8},{9},{10},{11}),({12},{13},{14},{15}))",
				           m [0], m [1], m [2], m [3],
				           m [4], m [5], m [6], m [7],
				           m [8], m [9], m [10], m [11],
				           m [12], m [13], m [14], m [15]
				           );
			}
			WriteLine ("S.link(o)");
			
			if (mf != null && mf.sharedMesh != null && trfm.light == null) {
				if (trfm.renderer != null) {
					var material = trfm.renderer.sharedMaterial;
					
					//if (trfm.lightmapIndex!=255)
					//{
					//	//var to = trfm.renderer.lightmapTilingOffset
					//}
					
					ExportMaterial (material);
					WriteLine ("if len(o.material_slots)<1:");
					WriteLine ("\to.data.materials.append(ma)");
					
					if (material.mainTexture != null && mf.sharedMesh.uv.Length > 0) {
						WriteLine ("i=ma.texture_slots[0].texture.image");
						WriteLine ("for f in me.uv_textures[0].data:");
						WriteLine ("\tf.image=i");
					}
					
					//WriteLine ("uvt = bpy.data.meshes['{0}'].uv_textures.new(name='UV')",id);
					//WriteLine ("\tuv_texture.name = 'UV'");
					//WriteLine("\tif bpy.data.meshes.get('{0}') is not None and len(bpy.data.meshes['{0}'].uv_textures)>0:",mf.ID(),mf.ID());
					//WriteLine("\t\tfor f in bpy.data.meshes['{0}'].uv_textures[0].data:",mf.ID());
					//WriteLine("\t\t\tf.image = image");
				}
			}
			
			if (trfm.audio != null) {
				WriteLine ("# audio");
			}
			
			if (trfm.collider != null) {
				WriteLine ("# collider");
			}
			
			if (trfm.collider2D != null) {
				WriteLine ("# collider2D");
			}
			
			if (trfm.particleEmitter != null) {
				var e = trfm.particleEmitter;
				WriteLine("o.beerengine_asset_type='8'");
				WriteLine("o.beerengine_emitter_one_shot=False");
				WriteLine("o.beerengine_emitter_texture='sprites/Steam_A.png'");
				WriteLine("o.beerengine_emitter_particlesystem='ps'");
				WriteLine("o.beerengine_emitter_ipolmode='0'");
				WriteLine("o.beerengine_emitter_animated=False");
				WriteLine("o.beerengine_emitter_usenormal=True");
				WriteLine("o.beerengine_emitter_alphafade=True");
				WriteLine("o.beerengine_emitter_rows=1");
				WriteLine("o.beerengine_emitter_cols=1");
				WriteLine("o.beerengine_emitter_velocity=[{0},{1},{2}]",e.localVelocity.x,e.localVelocity.y,e.localVelocity.z);
				//WriteLine("o.beerengine_emitter_force=[0,0,0]");
				WriteLine("o.beerengine_emitter_count={0}",100);//e.particleCount);
				/*
				WriteLine("o.beerengine_emitter_framerate=15");
				WriteLine("o.beerengine_emitter_duration=1");
				WriteLine("o.beerengine_emitter_delay=0");
				WriteLine("o.beerengine_emitter_alpha_fade_in=0");
				WriteLine("o.beerengine_emitter_alpha_fade_out=0");
				WriteLine("o.beerengine_emitter_alpha_value=0");
				WriteLine("o.beerengine_emitter_damping=0");
				WriteLine("o.beerengine_emitter_alpha_begin=0");
				WriteLine("o.beerengine_emitter_alpha_end=0");
				WriteLine("o.beerengine_emitter_size_min=0");
				WriteLine("o.beerengine_emitter_size_max=0");
				WriteLine("o.beerengine_emitter_size_inc=0");
				WriteLine("o.beerengine_emitter_rot_inc=0");
				WriteLine("o.beerengine_emitter_rot_min=0");
				WriteLine("o.beerengine_emitter_rot_max=0");
				*/
				WriteLine("o.beerengine_emitter_energy_min={0}",e.minEnergy);
				WriteLine("o.beerengine_emitter_energy_max={0}",e.maxEnergy);
				WriteLine("o.beerengine_emitter_emission_min={0}",e.minEmission);
				WriteLine("o.beerengine_emitter_emission_max={0}",e.maxEmission);
				//WriteLine("o.beerengine_emitter_velocity_min=[{0},{2},{3}]",e.localVelocity.X,e.localVelocity.Y,e.localVelocity.Z);
				//WriteLine("o.beerengine_emitter_velocity_max=[{0},{2},{3}]",e.localVelocity.X,e.localVelocity.Y,e.localVelocity.Z);
			}
			
			
			
		});
		
		/*
		var i = 0;
		foreach (var m in meshedobjects) {
			WriteLine ("bpy.ops.object.select_pattern(pattern='{0}')",m);
			WriteLine ("bpy.context.scene.objects.active=bpy.context.scene.objects['{0}']",m);
			WriteLine ("bpy.ops.object.editmode_toggle()");
			WriteLine ("bpy.ops.mesh.select_all(action='SELECT')");
			WriteLine ("bpy.ops.mesh.remove_doubles()");
			WriteLine ("bpy.ops.mesh.faces_shade_smooth()");
			WriteLine ("bpy.ops.object.editmode_toggle()");
			WriteLine ("print('{0} / {1} {2}')",i,meshedobjects.Count,m);
		}
		*/
		
		
		
		script.Close ();
	}
	
	void Write (string format, params object[] arg)
	{
		script.Write (format, arg);
	}
	
	void WriteLine (string format, params object[] arg)
	{
		script.WriteLine (format, arg);
	}
}

public class Driver : ScriptableObject
{
	[MenuItem ("File/Export to Blender")]
	static void Export ()
	{
		var be = new BlenderExporter ();
		be.Export ();
	}
	
}
