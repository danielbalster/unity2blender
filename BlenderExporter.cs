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
		return self.name.Replace ("Instance","") + "#" + self.GetInstanceID ();
	}
}

namespace BeerEngine
{
	public enum SgAssetType
	{
		IMAGE=0,
		MATERIAL=1,
		LIGHT=2,
		GEOMETRY=3,
		CONTROLLER=4,
		CAMERA=5,
		NAVMESH=6,
		SKELETON=7,
		EMITTER=8,
		PARTICLESYSTEM=9,
		AUDIOSOURCE=10,
		FORCEFIELD=11,
		TRIGGERVOLUME=12,
		SKYBOX=13,
		MAX
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
		} else if (go.type == LightType.Directional) {
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
	
	public void ExportMesh (Mesh amesh, Mesh mesh, Material mat, Renderer renderer)
	{
		int csum = 0;
		
		// expensive and lame
		if (mesh.vertexCount > 0) {
			for (int i = 0, n = mesh.vertexCount; i < n; ++i) {
				var v = mesh.vertices [i];
				var x = BitConverter.GetBytes (v.x);
				var y = BitConverter.GetBytes (v.y);
				var z = BitConverter.GetBytes (v.z);
				for (int j=0; j<4; ++j) {
					csum = unchecked(csum + x [j]);
					csum = unchecked(csum + y [j]);
					csum = unchecked(csum + z [j]);
				}
			}
		}
		
		var id = mesh.name.Replace ("Instance","") + "#" + csum;
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
				// inverted
				verts.AppendFormat ("({0},{1},{2}),", -v.x, -v.y, -v.z);
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
		if (mesh.uv2.Length > 0) {
			Write ("uv2=[");
			//var lm = renderer.lightmapTilingOffset;
			//Debug.Log (string.Format ("i={0} xyzw={1}",renderer.lightmapIndex,renderer.lightmapTilingOffset));
			for (int i = 0, n = mesh.uv2.Length; i < n; ++i) {
				var uv = mesh.uv2 [i];
				Write ("({0},{1}),", uv.x, uv.y);
//				Write ("({0},{1}),", (uv.x*lm.x)+lm.z, (uv.y*lm.y)+lm.w);
			}
			WriteLine ("]");
		}
		int[] tris = mesh.triangles;
		for (int i = 0, n = tris.Length; i < n; i+=3) {
			// normal flipped
			faces.AppendFormat ("({0},{1},{2}),", tris [i + 2], tris [i + 1], tris [i + 0]);
		}
		WriteLine ("me.from_pydata([{0}],[],[{1}])", verts.ToString (), faces.ToString ());
		if (mesh.uv != null && mesh.uv.Length > 0) {
			WriteLine ("uvt=me.uv_textures.new(name='UVMap')");
			WriteLine ("uvl=me.uv_layers[0].data");
			WriteLine ("for p in me.polygons:");
			WriteLine ("\tfor l in range(p.loop_start,p.loop_start+p.loop_total):");
			WriteLine ("\t\tuvl[l].uv=uv1[me.loops[l].vertex_index]");
		}
		if (mesh.uv2 != null && mesh.uv2.Length > 0) {
			WriteLine ("uvt=me.uv_textures.new(name='LightMap')");
			WriteLine ("uvl=me.uv_layers[1].data");
			WriteLine ("for p in me.polygons:");
			WriteLine ("\tfor l in range(p.loop_start,p.loop_start+p.loop_total):");
			WriteLine ("\t\tuvl[l].uv=uv2[me.loops[l].vertex_index]");
		}

	}

	int uniqueId = 0;

	public void CreateTriggerVolume (Vector3 o, Vector3 _s)
	{
		var id = "tv" + uniqueId;
		uniqueId++;
		WriteLine ("me=Me.new('{0}')", id);
		var verts = new StringBuilder ();
		var faces = new StringBuilder ();


		var s = new Vector3 (_s.x * 0.5f, _s.y * 0.5f, _s.z * 0.5f);

		verts.AppendFormat ("({0},{1},{2}),", o.x - s.x, o.y - s.y, o.z + s.z);
		verts.AppendFormat ("({0},{1},{2}),", o.x - s.x, o.y + s.y, o.z + s.z);
		verts.AppendFormat ("({0},{1},{2}),", o.x + s.x, o.y + s.y, o.z + s.z);
		verts.AppendFormat ("({0},{1},{2}),", o.x + s.x, o.y - s.y, o.z + s.z);
		verts.AppendFormat ("({0},{1},{2}),", o.x - s.x, o.y - s.y, o.z - s.z);
		verts.AppendFormat ("({0},{1},{2}),", o.x - s.x, o.y + s.y, o.z - s.z);
		verts.AppendFormat ("({0},{1},{2}),", o.x + s.x, o.y + s.y, o.z - s.z);
		verts.AppendFormat ("({0},{1},{2}),", o.x + s.x, o.y - s.y, o.z - s.z);
/*
		verts.AppendFormat ("({0},{1},{2}),", -s.x, -s.y, +s.z);
		verts.AppendFormat ("({0},{1},{2}),", -s.x, +s.y, +s.z);
		verts.AppendFormat ("({0},{1},{2}),", +s.x, +s.y, +s.z);
		verts.AppendFormat ("({0},{1},{2}),", +s.x, -s.y, +s.z);
		verts.AppendFormat ("({0},{1},{2}),", -s.x, -s.y, -s.z);
		verts.AppendFormat ("({0},{1},{2}),", -s.x, +s.y, -s.z);
		verts.AppendFormat ("({0},{1},{2}),", +s.x, +s.y, -s.z);
		verts.AppendFormat ("({0},{1},{2}),", +s.x, -s.y, -s.z);
*/
		faces.AppendFormat ("({2},{1},{0}),", 0, 1, 2); // front
		faces.AppendFormat ("({2},{1},{0}),", 2, 3, 0);
		faces.AppendFormat ("({2},{1},{0}),", 1, 5, 6); // top
		faces.AppendFormat ("({2},{1},{0}),", 6, 2, 1);
		faces.AppendFormat ("({2},{1},{0}),", 5, 4, 7); // back
		faces.AppendFormat ("({2},{1},{0}),", 7, 6, 5);
		faces.AppendFormat ("({2},{1},{0}),", 4, 0, 3); // bottom
		faces.AppendFormat ("({2},{1},{0}),", 3, 7, 4);
		faces.AppendFormat ("({2},{1},{0}),", 3, 2, 6); // right
		faces.AppendFormat ("({2},{1},{0}),", 6, 7, 3);
		faces.AppendFormat ("({2},{1},{0}),", 0, 5, 1); // left
		faces.AppendFormat ("({2},{1},{0}),", 0, 4, 5);


		WriteLine ("me.from_pydata([{0}],[],[{1}])", verts.ToString (), faces.ToString ());
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
	
	void ExportMaterial (Material mat, Renderer renderer)
	{
		if (renderer.lightmapIndex < 254) {
			WriteLine ("o.beerengine_lightmap_index={0}", renderer.lightmapIndex);
			WriteLine ("o.beerengine_lightmap_address=[{0},{1},{2},{3}]", renderer.lightmapScaleOffset.x, renderer.lightmapScaleOffset.y, renderer.lightmapScaleOffset.z, renderer.lightmapScaleOffset.w);
		}
		
		if (materials.Contains (mat.ID ())) {
			WriteLine ("ma=Ma['{0}']", mat.ID ());
			return;
		}
		materials.Add (mat.ID ());
		WriteLine ("ma=Ma.new('{0}')", mat.ID ());
		
		WriteLine ("ma.diffuse_color={0},{1},{2}", mat.color.r, mat.color.g, mat.color.b);
		
		bool additive = mat.shader.name.ToLower ().Contains ("additive");
		bool cutout = mat.shader.name.ToLower ().Contains ("cutout");
		
		if (cutout) {
			WriteLine ("ma.use_transparency=True");
			WriteLine ("ma.game_settings.alpha_blend='CLIP'");
		}
		if (additive) {
			WriteLine ("ma.use_transparency=True");
			WriteLine ("ma.game_settings.alpha_blend='ADD'");
//#if BEERENGINE
			WriteLine ("ma.beerengine_cull_enable=False");
			WriteLine ("ma.beerengine_blend_func_src='GL_ONE'");
			WriteLine ("ma.beerengine_blend_func_dst='GL_ONE'");
			WriteLine ("ma.beerengine_blend_func_src_a='GL_ONE'");
			WriteLine ("ma.beerengine_blend_func_dst_a='GL_ONE'");
//#endif
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


		if (renderer.lightmapIndex>-1 && renderer.lightmapIndex<LightmapSettings.lightmaps.Length) {
			if (ExportTexture( LightmapSettings.lightmaps[renderer.lightmapIndex].lightmapFar ))
			{
				WriteLine("tslm=ma.texture_slots.add() # lightmap");
				WriteLine("tslm.texture=t");
				WriteLine("tslm.texture_coords='UV'");
				WriteLine("tslm.uv_layer='LightMap'");
				WriteLine("tslm.use_map_color_diffuse=True");
			}
		}

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

		foreach (var v in Enum.GetValues(typeof(BeerEngine.SgAssetType))) {
			WriteLine ("BE_{0}='{1}'", v,(int)v);
		}

		var meshedobjects = new HashSet<string> ();
		
		Recurse ((trfm) => {

			// blender is "one component per transform node"

			var type = BeerEngine.SgAssetType.MAX;

			var name = "None";

			if (trfm.GetComponent<Light>() != null)
				type = BeerEngine.SgAssetType.LIGHT;
			if (trfm.GetComponent<ParticleEmitter>() != null)
				type = BeerEngine.SgAssetType.EMITTER;
			if (trfm.GetComponent<Camera>() != null)
				type = BeerEngine.SgAssetType.CAMERA;

			var mf = trfm.GetComponent<MeshFilter> ();

			if (type == BeerEngine.SgAssetType.MAX) {
				if (mf != null && mf.sharedMesh != null) {
					Material mat = null;
					if (trfm.GetComponent<Renderer>() != null) {
						mat = trfm.GetComponent<Renderer>().sharedMaterial;
					}
				


					ExportMesh (mf.mesh, mf.sharedMesh, mat, trfm.GetComponent<Renderer>());
					type = BeerEngine.SgAssetType.GEOMETRY;
					name = "me";
					meshedobjects.Add (trfm.ID ());
				}
			}
			
			if (type == BeerEngine.SgAssetType.MAX) {
				if (trfm.GetComponent<Collider>() != null
					&& trfm.GetComponent<AudioSource>() == null
					&& trfm.GetComponent<Collider>() is BoxCollider
				    ) {
					type = BeerEngine.SgAssetType.TRIGGERVOLUME;
				}
			}

			if (type == BeerEngine.SgAssetType.LIGHT) {
				ExportLight (trfm.GetComponent<Light>());
				name = "l";
			}

			if (type == BeerEngine.SgAssetType.TRIGGERVOLUME) {
				//WriteLine ("# collider");
				//CreateTriggerVolume (trfm.collider.bounds.center, trfm.collider.bounds.extents);
				//name = "me";
				//WriteLine ("o.empty_draw_type='CUBE'");
				//WriteLine ("o.empty_draw_size={0}",trfm.collider.bounds.extents.z);
			}
			


			WriteLine ("o=O.new('{0}',{1})", trfm.ID (), name);
			
			if (name == "None") {
				WriteLine ("o.empty_draw_type='SINGLE_ARROW'");
			}

			if (type == BeerEngine.SgAssetType.TRIGGERVOLUME) {
				WriteLine ("o.beerengine_asset_type=BE_TRIGGERVOLUME");
				WriteLine ("o.beerengine_triggervolume_center=[{0},{1},{2}]", trfm.GetComponent<Collider>().bounds.center.x, trfm.GetComponent<Collider>().bounds.center.y, trfm.GetComponent<Collider>().bounds.center.z);
				WriteLine ("o.beerengine_triggervolume_extents=[{0},{1},{2}]", trfm.GetComponent<Collider>().bounds.extents.x, trfm.GetComponent<Collider>().bounds.extents.y, trfm.GetComponent<Collider>().bounds.extents.z);
				WriteLine ("o.show_name = True");
				WriteLine ("o.draw_type = 'BOUNDS'");
			}

			if (trfm.parent != null) {
				// invert coordinate system
				WriteLine ("o.location=({0},{1},{2})", -trfm.localPosition.x, -trfm.localPosition.y, -trfm.localPosition.z);
				WriteLine ("o.scale=({0},{1},{2})", trfm.localScale.x, trfm.localScale.y, trfm.localScale.z);
				WriteLine ("o.rotation_mode=Q");
				WriteLine ("o.rotation_quaternion=[{0},{1},{2},{3}]", trfm.localRotation.w, trfm.localRotation.x, trfm.localRotation.y, trfm.localRotation.z);
				WriteLine ("if O['{0}'] is not None:", trfm.parent.ID ());
				WriteLine ("\to.parent=O['{0}']", trfm.parent.ID ());
			} else {
				// rotate root nodes 90° ccw on x-axis (unity -> blender coordinate system)
				var m = Matrix4x4.TRS (trfm.localPosition, trfm.localRotation, trfm.localScale);
				var flip_x = new Vector3 (1, 1, 1);
				m = Matrix4x4.TRS (Vector3.zero, Quaternion.Euler (-90, 0, 0), flip_x) * m;
				WriteLine ("o.matrix_local=(({0},{1},{2},{3}),({4},{5},{6},{7}),({8},{9},{10},{11}),({12},{13},{14},{15}))",
				           m [0], m [1], m [2], m [3],
				           m [4], m [5], m [6], m [7],
				           m [8], m [9], m [10], m [11],
				           m [12], m [13], m [14], m [15]
				);
			}
			WriteLine ("S.link(o)");
			
			if (type == BeerEngine.SgAssetType.GEOMETRY) {
				if (trfm.GetComponent<Renderer>() != null) {
					var material = trfm.GetComponent<Renderer>().sharedMaterial;
					
					//if (trfm.lightmapIndex!=255)
					//{
					//	//var to = trfm.renderer.lightmapTilingOffset
					//}
					
					ExportMaterial (material, trfm.GetComponent<Renderer>());
					WriteLine ("if len(o.material_slots)<1:");
					WriteLine ("\to.data.materials.append(ma)");
					
					if (material.mainTexture != null && mf.sharedMesh.uv.Length > 0) {
						WriteLine ("i=ma.texture_slots[0].texture.image");
						WriteLine ("for f in me.uv_textures[0].data:");
						WriteLine ("\tf.image=i");
					}
					if (trfm.GetComponent<Renderer>().lightmapIndex>-1 && trfm.GetComponent<Renderer>().lightmapIndex<LightmapSettings.lightmaps.Length) {
						WriteLine ("i=tslm.texture.image");
						WriteLine ("for f in me.uv_textures[len(me.uv_textures)-1].data:");
						WriteLine ("\tf.image=i");
					}

					//WriteLine ("uvt = bpy.data.meshes['{0}'].uv_textures.new(name='UV')",id);
					//WriteLine ("\tuv_texture.name = 'UV'");
					//WriteLine("\tif bpy.data.meshes.get('{0}') is not None and len(bpy.data.meshes['{0}'].uv_textures)>0:",mf.ID(),mf.ID());
					//WriteLine("\t\tfor f in bpy.data.meshes['{0}'].uv_textures[0].data:",mf.ID());
					//WriteLine("\t\t\tf.image = image");
				}
			}

			if (type == BeerEngine.SgAssetType.AUDIOSOURCE) {
				WriteLine ("# audio");
			}
			
			if (type == BeerEngine.SgAssetType.EMITTER) {
				var e = trfm.GetComponent<ParticleEmitter>();
//#if BEERENGINE
				WriteLine ("o.beerengine_asset_type=BE_EMITTER");
				WriteLine ("o.beerengine_emitter_one_shot=False");


				var path = UnityEditor.AssetDatabase.GetAssetOrScenePath (e.GetComponent<Renderer>().sharedMaterial.mainTexture);

				WriteLine ("o.beerengine_emitter_texture='{0}'", path);
				WriteLine ("o.beerengine_emitter_particlesystem='ps'");
				WriteLine ("o.beerengine_emitter_ipolmode='0'");
				WriteLine ("o.beerengine_emitter_animated=False");
				WriteLine ("o.beerengine_emitter_usenormal=False");
				WriteLine ("o.beerengine_emitter_alphafade=True");
				WriteLine ("o.beerengine_emitter_rows=1");
				WriteLine ("o.beerengine_emitter_cols=1");
				//WriteLine ("o.beerengine_emitter_ipolmode=0");
				WriteLine ("o.beerengine_emitter_velocity=[{0},{1},{2}]", -e.localVelocity.x, -e.localVelocity.y, -e.localVelocity.z);
				WriteLine ("o.beerengine_emitter_force=[{0},{1},{2}]", -e.localVelocity.x, -e.localVelocity.y, -e.localVelocity.z);
				//WriteLine("o.beerengine_emitter_force=[0,4,0]");
				WriteLine ("o.beerengine_emitter_velocity_min=1");
				WriteLine ("o.beerengine_emitter_velocity_max=1");
				WriteLine ("o.beerengine_emitter_count={0}", 30);//e.particleCount);
				/*
				WriteLine("o.beerengine_emitter_framerate=15");
				WriteLine("o.beerengine_emitter_duration=1");
				WriteLine("o.beerengine_emitter_delay=0");
				*/
				WriteLine ("o.beerengine_emitter_alpha_fade_in=0.1");
				WriteLine ("o.beerengine_emitter_alpha_fade_out=1");
				WriteLine ("o.beerengine_emitter_alpha_value=0.5");
				WriteLine ("o.beerengine_emitter_damping=0.7");
				WriteLine ("o.beerengine_emitter_alpha_begin=0");
				WriteLine ("o.beerengine_emitter_alpha_end=0");
				WriteLine ("o.beerengine_emitter_size_min=1");
				WriteLine ("o.beerengine_emitter_size_max=1");
				WriteLine ("o.beerengine_emitter_size_inc=1");
				WriteLine ("o.beerengine_emitter_rot_inc=1");
				WriteLine ("o.beerengine_emitter_rot_min=0");
				WriteLine ("o.beerengine_emitter_rot_max=360");

				WriteLine ("o.beerengine_emitter_energy_min={0}", e.minEnergy);
				WriteLine ("o.beerengine_emitter_energy_max={0}", e.maxEnergy);
				WriteLine ("o.beerengine_emitter_emission_min={0}", e.minEmission);
				WriteLine ("o.beerengine_emitter_emission_max={0}", e.maxEmission);
				//WriteLine("o.beerengine_emitter_velocity_min=[{0},{2},{3}]",e.localVelocity.X,e.localVelocity.Y,e.localVelocity.Z);
				//WriteLine("o.beerengine_emitter_velocity_max=[{0},{2},{3}]",e.localVelocity.X,e.localVelocity.Y,e.localVelocity.Z);
//#endif
			}
			
			
			
		});
		
//#if BEERENGINE
		// add default particle system
		WriteLine ("o=O.new('ps',None)");
		WriteLine ("o.empty_draw_type='SINGLE_ARROW'");
		WriteLine ("o.beerengine_asset_type=BE_PARTICLESYSTEM");
		WriteLine ("o.beerengine_psys_count=65536");
		WriteLine ("o.beerengine_psys_vs_shader='shaders/vs/particles.glsl'");
		WriteLine ("o.beerengine_psys_fs_shader='shaders/fs/particles.glsl'");
		WriteLine ("S.link(o)");
//#endif		
		/*
		 * this is really expensive

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
