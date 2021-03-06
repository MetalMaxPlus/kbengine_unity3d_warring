using UnityEngine;
using KBEngine;
using System.Collections;
using System;
using System.Xml;
using System.Collections.Generic;

public class SceneRenderSettings
{
	public bool fog = false;
	public FogMode fogMode = FogMode.Linear;
	public float fogDensity = 0.0f;
	public float fogStartDistance = 0;
	public float fogEndDistance = 0;
	public Color fogColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
	public Color ambientLight = new Color(0.0f, 0.0f, 0.0f, 0.0f);
	public float haloStrength = 0.0f;
	public float flareStrength = 0.0f;
	public string skyboxname;
}

public class Scene
{
	public static Dictionary<string, Asset> assetsCache = new Dictionary<string, Asset>();
	public Dictionary<string, SceneObject> objs = new Dictionary<string, SceneObject>();
	public Dictionary<string, SceneObject> persisted_objs = new Dictionary<string, SceneObject>();
	public string name;
	public XmlDocument xmlDoc = null;
	private loader loader_;
	private bool created = false;
	private SceneRenderSettings renderSetting = new SceneRenderSettings();
	public WorldManager worldmgr = null;
	
	public Scene(string scenename, loader l)
	{
		name = scenename;
		loader_ = l;
	}
	
	void onLoadSceneXMLCompleted(bool autocreate)
	{
		if(autocreate)
			create();
	}
	
	IEnumerator loadSceneXML(bool autocreate)
	{
		Common.DEBUG_MSG("starting loadSceneXML(" + name + ")...");
		WWW www = new WWW(Common.safe_url("/StreamingAssets/" + name + ".xml"));  
		yield return www; 
		
        if (www.error != null)  
            Common.ERROR_MSG(www.error);  

		string xml = www.text;
		if(xmlDoc == null)
		{
			xmlDoc = new XmlDocument();
			xmlDoc.LoadXml(xml);  
		}
		
		loadingbar.loadingbar_currValue += 1;
		Common.DEBUG_MSG("loadSceneXML(" + name + ") is finished!");
		onLoadSceneXMLCompleted(autocreate);
	}
	
	public void loadScene(bool autocreate, bool showProgressbar)
	{
		if(loadingbar.loadingbar_show == true)
		{
			Common.WARNING_MSG("Scene::loadScene: (" + name + ") is in loading...");
			return;
		}
		
		loadingbar.reset(showProgressbar);
		
		if(xmlDoc == null)
		{
			loadingbar.loadingbar_maxValue += 1;
			loader_.StartCoroutine(loadSceneXML(autocreate));
		}
		else{
			onLoadSceneXMLCompleted(autocreate);
		}
	}
	
	public void unload()
	{
		created = false;
		loader_.loadPool.clear(false);
		loadingbar.reset(false);
		
		if(worldmgr != null)
		{
			worldmgr.unload();
			worldmgr = null;
		}
	
		foreach(KeyValuePair<string, SceneObject> e in objs)
		{
			if(e.Value.asset.unloadLevel == Asset.UNLOAD_LEVEL.LEVEL_FORBID_GAMEOBJECT)
			{
				Common.DEBUG_MSG("SceneObject(" + e.Value.asset.source + ") is persisted!");
				
				SceneObject v = null;
				if(!persisted_objs.TryGetValue(e.Key, out v))
				{
					persisted_objs.Add(e.Key, e.Value);
				}
				else
				{
					Common.DEBUG_MSG("SceneObject(" + e.Value.asset.source + ") is exist!");
				}
				
				continue;
			}
			
			UnityEngine.Object.Destroy(e.Value.gameObject);
			e.Value.gameObject = null;
			
			/* 等新场景创建完毕后再判断是否被重新引用了
			   如果仍然没有被引用则可认为此资源是应该被卸载的
			   这里我们只减引用就好了
			*/
			
			Common.DEBUG_MSG("destroyed sceneobject(" + e.Value.name + ") successfully!");
			e.Value.asset.refs.Remove(e.Value.idkey);
			e.Value.asset = null;
		}
		
		objs.Clear();
		Common.DEBUG_MSG("unload scene(" + name + ") successfully!");
	}
	
	void setupWorld(XmlElement gameobject)
	{
		worldmgr = new WorldManager();
		worldmgr.parentScene = this;
		worldmgr.name = gameobject.GetAttribute("wname");
		
		foreach(XmlElement child in gameobject.ChildNodes)
		{
			if(child.GetAttribute("name") == "Terrain")
			{
				worldmgr.terrainName = child.SelectSingleNode("name").InnerText;
				
				string[] ssize = child.SelectSingleNode("size").InnerText.Split(new char[]{' '});
				worldmgr.size.x = float.Parse(ssize[0]);
				worldmgr.size.y = float.Parse(ssize[1]);
				worldmgr.size.z = float.Parse(ssize[2]);
				
				worldmgr.chunkSplit = int.Parse(child.SelectSingleNode("splitSize").InnerText);
				worldmgr.chunkSize = worldmgr.size.x / worldmgr.chunkSplit;
				
				Common.DEBUG_MSG("name:" + worldmgr.name + ", worldsize:" + worldmgr.size + ", splitChunks:" + 
					worldmgr.chunkSplit + ", chunkSize:" + (worldmgr.size.x / worldmgr.chunkSplit));
				
				foreach(XmlElement tchild in child.SelectSingleNode("splatprotos").ChildNodes)
				{
					KeyValuePair<string, string> p = new KeyValuePair<string, string>(tchild.GetAttribute("texture"), tchild.GetAttribute("normalMap"));
					worldmgr.load_splatPrototypes.Add(p);
					
					KeyValuePair<Vector2, Vector2> p1 = new KeyValuePair<Vector2, Vector2>(
						new Vector2(
						float.Parse(tchild.GetAttribute("tileSizeX")), 
						float.Parse(tchild.GetAttribute("tileSizeY"))),
						new Vector2(
						float.Parse(tchild.GetAttribute("tileOffsetX")), 
						float.Parse(tchild.GetAttribute("tileOffsetY")))
						);
					
					worldmgr.splatPrototypes_titlesizeoffset.Add(p1);
					Common.DEBUG_MSG("set load splatPrototype:" + tchild.GetAttribute("texture"));
				}
				
				foreach(XmlElement tchild in child.SelectSingleNode("treePrototypes").ChildNodes)
				{
					worldmgr.load_treePrototypes.Add(tchild.GetAttribute("prefab"));
					Common.DEBUG_MSG("set load treePrototype:" + tchild.GetAttribute("prefab"));
				}
				
				foreach(XmlElement tchild in child.SelectSingleNode("detailPrototypes").ChildNodes)
				{
					worldmgr.load_detailPrototypes.Add(tchild.GetAttribute("prefab"));
					Common.DEBUG_MSG("set load detailPrototype:" + tchild.GetAttribute("prefab"));
				}
				
				worldmgr.createWorldObjs();
			}
			else
			{
				WorldSceneObject obj = new WorldSceneObject();
				
				Asset newasset = findAsset(child.GetAttribute("asset"), true, child.GetAttribute("layer"));
				
				Vector3 pos = Vector3.zero;  
				Vector3 rot = Vector3.zero;  
				Vector3 sca = Vector3.zero;   
				
				XmlNode gameobjectpos = child.SelectSingleNode("transform").SelectSingleNode("position");
				XmlNode gameobjectrot = child.SelectSingleNode("transform").SelectSingleNode("rotation");
				XmlNode gameobjectsca = child.SelectSingleNode("transform").SelectSingleNode("scale");
				
				pos.x = float.Parse(gameobjectpos.SelectSingleNode("x").InnerText);
				pos.y = float.Parse(gameobjectpos.SelectSingleNode("y").InnerText);
				pos.z = float.Parse(gameobjectpos.SelectSingleNode("z").InnerText);
				
				rot.x = float.Parse(gameobjectrot.SelectSingleNode("x").InnerText);
				rot.y = float.Parse(gameobjectrot.SelectSingleNode("y").InnerText);
				rot.z = float.Parse(gameobjectrot.SelectSingleNode("z").InnerText);
				
				sca.x = float.Parse(gameobjectsca.SelectSingleNode("x").InnerText);
				sca.y = float.Parse(gameobjectsca.SelectSingleNode("y").InnerText);
				sca.z = float.Parse(gameobjectsca.SelectSingleNode("z").InnerText);
			
				obj.name = child.GetAttribute("name");
				obj.position = pos;
				obj.eulerAngles = rot;
				obj.scale = sca;
				obj.idkey = child.GetAttribute("id");
				obj.asset = newasset;
				newasset.loading = false;
				newasset.type = Asset.TYPE.WORLD_OBJ;
				newasset.loadLevel = Asset.LOAD_LEVEL.LEVEL_SCRIPT_DYNAMIC;
				// newasset.refs.Add(obj.idkey);
				
				WorldManager.ChunkPos chunkpos = WorldManager.calcAtChunk(pos.x, pos.z, worldmgr.chunkSplit, worldmgr.chunkSize);
				worldmgr.worldObjs[chunkpos.x, chunkpos.y, 0].Add(obj);
			}
		}

		worldmgr.load();
	}
	
	void setRender(XmlElement gameobject)
	{
		renderSetting.fog = false;
		if(gameobject.GetAttribute("fog") == "true")
			renderSetting.fog = true;
		
		int mode = int.Parse(gameobject.GetAttribute("fogMode"));

		switch(mode)
		{
		case 1:
			renderSetting.fogMode = FogMode.Linear;
			break;
		case 2:
			renderSetting.fogMode = FogMode.Exponential;
			break;
		case 3:
			renderSetting.fogMode = FogMode.ExponentialSquared;
			break;
		default:
			renderSetting.fogMode = FogMode.Linear;
			break;
		};

		renderSetting.fogStartDistance = float.Parse(gameobject.GetAttribute("fogStartDistance"));
		renderSetting.fogEndDistance = float.Parse(gameobject.GetAttribute("fogEndDistance"));
		renderSetting.fogDensity = float.Parse(gameobject.GetAttribute("fogDensity"));
		renderSetting.haloStrength = float.Parse(gameobject.GetAttribute("haloStrength"));
		renderSetting.flareStrength = float.Parse(gameobject.GetAttribute("flareStrength"));

		renderSetting.fogColor.r = float.Parse(gameobject.GetAttribute("fogColor_r"));
		renderSetting.fogColor.g = float.Parse(gameobject.GetAttribute("fogColor_g"));
		renderSetting.fogColor.b = float.Parse(gameobject.GetAttribute("fogColor_b"));
		renderSetting.fogColor.a = float.Parse(gameobject.GetAttribute("fogColor_a"));

		renderSetting.ambientLight.r = float.Parse(gameobject.GetAttribute("ambientLight_r"));
		renderSetting.ambientLight.g = float.Parse(gameobject.GetAttribute("ambientLight_g"));
		renderSetting.ambientLight.b = float.Parse(gameobject.GetAttribute("ambientLight_b"));
		renderSetting.ambientLight.a = float.Parse(gameobject.GetAttribute("ambientLight_a"));
		
		renderSetting.skyboxname = gameobject.GetAttribute("skybox");

		RenderSettings.fog = renderSetting.fog;
		RenderSettings.ambientLight = renderSetting.ambientLight;
		RenderSettings.fogColor = renderSetting.fogColor;
		RenderSettings.fogDensity = renderSetting.fogDensity;
		RenderSettings.fogStartDistance = renderSetting.fogStartDistance;
		RenderSettings.fogEndDistance = renderSetting.fogEndDistance;
		RenderSettings.fogMode = renderSetting.fogMode;
		RenderSettings.haloStrength = renderSetting.haloStrength;
		RenderSettings.flareStrength = renderSetting.flareStrength;

		Common.DEBUG_MSG("RenderSettings.fog=" + RenderSettings.fog);
		Common.DEBUG_MSG("RenderSettings.ambientLight=" + RenderSettings.ambientLight);
		Common.DEBUG_MSG("RenderSettings.fogColor=" + RenderSettings.fogColor);
		Common.DEBUG_MSG("RenderSettings.fogDensity=" + RenderSettings.fogDensity);
		Common.DEBUG_MSG("RenderSettings.fogStartDistance=" + RenderSettings.fogStartDistance);
		Common.DEBUG_MSG("RenderSettings.fogEndDistance=" + RenderSettings.fogEndDistance);
		Common.DEBUG_MSG("RenderSettings.fogMode=" + RenderSettings.fogMode);
		Common.DEBUG_MSG("RenderSettings.haloStrength=" + RenderSettings.haloStrength);
		Common.DEBUG_MSG("RenderSettings.flareStrength=" + RenderSettings.flareStrength);
		Common.DEBUG_MSG("RenderSettings.skybox=" + renderSetting.skyboxname);
		
		if(renderSetting.skyboxname.Length > 0)
		{
			Asset newasset1 = null;

			if(!assetsCache.TryGetValue(renderSetting.skyboxname, out newasset1))
			{
				newasset1 = new Asset();
				newasset1.source = renderSetting.skyboxname;
				newasset1.type = Asset.TYPE.SKYBOX;
				newasset1.loadLevel = Asset.LOAD_LEVEL.LEVEL_ENTER_BEFORE;
				newasset1.layerName = "Default";
				assetsCache.Add(renderSetting.skyboxname, newasset1);
			}
		}
	}
	
	public static Asset findAsset(string assetsource, bool autocreate, string layer)
	{
		Asset newasset = null;

		if(!assetsCache.TryGetValue(assetsource, out newasset))
		{
			if(autocreate == true)
			{
				newasset = new Asset();
				newasset.source = assetsource;
				if(layer != "")
					newasset.layerName = layer;
				assetsCache.Add(assetsource, newasset);
				Debug.Log("assetsCache:: new " + assetsource + ", layer=" + newasset.layerName);
				//foreach(string name in assetsCache.Keys)
				//{
				//	Debug.Log("assetsCache::" + name);
				//}
			}
		}
		
		return newasset;
	}
	
	public bool create()
	{
		if(xmlDoc == null || created == true)
		{
			Common.WARNING_MSG("Scene::create: (" + name + ") is failed! created=" + created);
			return false;
		}
	
		Common.DEBUG_MSG("create scene(" + name + ")...");
		created = true;
		
		string[] keys = new string[assetsCache.Keys.Count]; 
		assetsCache.Keys.CopyTo(keys, 0); 
		List<string> arrkeys = new List<string>(keys);
		
		XmlNodeList nodeList = xmlDoc.SelectSingleNode("root").ChildNodes;  
        foreach(XmlElement gameobject in nodeList)  
        {   
			string objname = gameobject.GetAttribute("name");

			if(objname == "renderSettings")
			{
				setRender(gameobject);
				continue;
			}
			
			if(objname == "world")
			{
				setupWorld(gameobject);
				continue;
			}
			
			string key = gameobject.GetAttribute("id");
			string assetsource = gameobject.GetAttribute("asset");
			UInt16 loadPri = UInt16.Parse(gameobject.GetAttribute("loadPri"));
			UInt16 loadLevel = UInt16.Parse(gameobject.GetAttribute("load"));
			UInt16 unloadLevel = UInt16.Parse(gameobject.GetAttribute("unload"));
			string layer = gameobject.GetAttribute("layer");
			
			for(int i=0; i<arrkeys.Count; i++)
			{
				if(arrkeys[i] == assetsource)
				{
					arrkeys.RemoveAt(i);
					break;
				}
			}
			
			Vector3 pos = Vector3.zero;  
			Vector3 rot = Vector3.zero;  
			Vector3 sca = Vector3.zero;   
			
			XmlNode gameobjectpos = gameobject.SelectSingleNode("transform").SelectSingleNode("position");
			XmlNode gameobjectrot = gameobject.SelectSingleNode("transform").SelectSingleNode("rotation");
			XmlNode gameobjectsca = gameobject.SelectSingleNode("transform").SelectSingleNode("scale");
			
			pos.x = float.Parse(gameobjectpos.SelectSingleNode("x").InnerText);
			pos.y = float.Parse(gameobjectpos.SelectSingleNode("y").InnerText);
			pos.z = float.Parse(gameobjectpos.SelectSingleNode("z").InnerText);
			
			rot.x = float.Parse(gameobjectrot.SelectSingleNode("x").InnerText);
			rot.y = float.Parse(gameobjectrot.SelectSingleNode("y").InnerText);
			rot.z = float.Parse(gameobjectrot.SelectSingleNode("z").InnerText);
			
			sca.x = float.Parse(gameobjectsca.SelectSingleNode("x").InnerText);
			sca.y = float.Parse(gameobjectsca.SelectSingleNode("y").InnerText);
			sca.z = float.Parse(gameobjectsca.SelectSingleNode("z").InnerText);
			
			Asset newasset = findAsset(assetsource, true, layer);
			SceneObject obj = new SceneObject();
			
			newasset.loadPri = loadPri;
			if(layer != "")
				newasset.layerName = layer;
			
			switch(loadLevel)
			{
				case 1:
					newasset.loadLevel = Asset.LOAD_LEVEL.LEVEL_ENTER_BEFORE;
					break;
				case 2:
					newasset.loadLevel = Asset.LOAD_LEVEL.LEVEL_ENTER_AFTER;
					break;
				case 3:
					newasset.loadLevel = Asset.LOAD_LEVEL.LEVEL_SCRIPT_DYNAMIC;
					break;
				default:
					newasset.loadLevel = Asset.LOAD_LEVEL.LEVEL_IDLE;
					break;
			};
			
			switch(unloadLevel)
			{
				case 1:
					newasset.unloadLevel = Asset.UNLOAD_LEVEL.LEVEL_FORBID;
					break;
				case 2:
					newasset.unloadLevel = Asset.UNLOAD_LEVEL.LEVEL_FORBID_GAMEOBJECT;
					break;
				default:
					newasset.unloadLevel = Asset.UNLOAD_LEVEL.LEVEL_NORMAL;
					break;
			};
					
			obj.name = objname;
			obj.position = pos;
			obj.eulerAngles = rot;
			obj.scale = sca;
			obj.idkey = key;
			obj.asset = newasset;
			newasset.refs.Add(key);
			
			if(newasset.bundle != null)
			{
				obj.Instantiate();
				newasset.refs.Remove(key);
			}
			
			objs.Add(key, obj);
        }
		
		for(int i=0; i<arrkeys.Count; i++)
		{
			Asset asset = assetsCache[arrkeys[i]];
			if(asset.unloadLevel == Asset.UNLOAD_LEVEL.LEVEL_FORBID || 
				asset.unloadLevel == Asset.UNLOAD_LEVEL.LEVEL_FORBID_GAMEOBJECT)
			{
				Common.DEBUG_MSG("assetBundle(" + asset.source + ") is persisted!");
				continue;
			}
			
			if(asset.createAtScene == name)
				continue;
			
			if(asset.bundle != null)
			{
				asset.bundle.Unload(true);
				Common.DEBUG_MSG("unload assetBundle(" + asset.source + ") successfully!");
			}
			else
			{
				Common.DEBUG_MSG("remove assetBundle(" + asset.source + ") successfully!");
			}
			
			assetsCache.Remove(arrkeys[i]);
		}

		Common.DEBUG_MSG("create scene(" + name + ") successfully, start loading...");

		foreach(KeyValuePair<string, Asset> e in Scene.assetsCache)
		{
			loader_.loadPool.addLoad(e.Value);
		}

		loader_.loadPool.start();
		return true;
	}
	
	public void addSceneObject(string key, SceneObject obj)
	{
		SceneObject sceneobj = null;
		if(objs.TryGetValue(key, out sceneobj))
		{
			removeSceneObject(key);
		}

		objs.Add(key, obj);
	}
	
	public void removeSceneObject(string key)
	{
		SceneObject sceneobj = null;
		if(!objs.TryGetValue(key, out sceneobj))
		{
			Common.ERROR_MSG("Scene::removeSceneObject: not found sceneObject(" + key + ")!");
			return;
		}
		
		UnityEngine.Object.Destroy(sceneobj.gameObject);
		sceneobj.gameObject = null;
		
		/* 等新场景创建完毕后再判断是否被重新引用了
		   如果仍然没有被引用则可认为此资源是应该被卸载的
		   这里我们只减引用就好了
		*/
		
		Common.DEBUG_MSG("Scene::removeSceneObject: destroyed sceneobject(" + sceneobj.idkey + ") successfully!");
		sceneobj.asset.refs.Remove(sceneobj.idkey);
		sceneobj.asset = null;	
		objs.Remove(sceneobj.idkey);	
	}
}
