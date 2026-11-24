using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("fr.rangedace.report", "REPOrt", "1.2.0")]
public sealed class REPOrt : BaseUnityPlugin
{
    static REPOrt mod;
    readonly Harmony patches = new Harmony("fr.rangedace.report");
    readonly Dictionary<string, ConfigEntry<bool>> flags = new Dictionary<string, ConfigEntry<bool>>();
    readonly string[] self = {"Unlimited Stamina","God Mode","No Tumble","Unlimited Battery","Non Enemy Targetable"};
    readonly string[] kinds = {"Player","Enemy","Item","Cart","Extraction","Death Head","Truck"};
    readonly Type[][] types = {
        new[]{typeof(PlayerAvatar)}, new[]{typeof(Enemy)}, new[]{typeof(ItemAttributes),typeof(ValuableObject)},
        new[]{typeof(PhysGrabCart)}, new[]{typeof(ExtractionPoint)}, new[]{typeof(PlayerDeathHead)}, new[]{typeof(TruckDoor)}
    };
    readonly List<Component>[] objects = Enumerable.Range(0,7).Select(i => new List<Component>()).ToArray();
    readonly HashSet<Enemy> frozen = new HashSet<Enemy>();
    ConfigEntry<int> bullets, fov;
    ConfigEntry<float> scale;
    bool open;
    int tab;
    Vector2 scroll;
    string damage = "10", status = "";
    float nextScan, nextFreeze;
    Rect window = new Rect(18,18,520,500);
    static FieldInfo Field(Type type,string name) { return AccessTools.Field(type,name); }
    static readonly FieldInfo hp = Field(typeof(PlayerHealth),"health"), maximum = Field(typeof(PlayerHealth),"maxHealth"),
        enemyHp = Field(typeof(EnemyHealth),"healthCurrent"), enemyHealth = Field(typeof(Enemy),"Health"),
        target = Field(typeof(Enemy),"TargetPlayerAvatar"), grabbed = Field(typeof(PhysGrabber),"grabbedPhysGrabObject");
    void Bind(string group,string key,bool value) { flags[key] = Config.Bind(group,key,value); }
    bool On(string key) { return flags[key].Value; }
    void Awake()
    {
        mod = this;
        Config.SaveOnConfigSet = false;
        Bind("Settings","Debug Mode",false);
        scale = Config.Bind("Settings","GUI Size",1f,new ConfigDescription("",new AcceptableValueRange<float>(0.6f,2f)));
        foreach(var key in self) Bind("Self",key,false);
        bullets = Config.Bind("Self","Gun Bullet Amount",0,new ConfigDescription("0 = game default",new AcceptableValueRange<int>(0,32)));
        fov = Config.Bind("Visuals","FOV",0,new ConfigDescription("0 = game default",new AcceptableValueRange<int>(0,140)));
        Bind("Visuals","Toggle ESP",false);
        foreach(var key in kinds) Bind("Visuals",key+" ESP",true);
        foreach (string read in new[]{"KeyDown","KeyUp","KeyHold","GetScrollY"})
            Prefix(typeof(InputManager),read,"AllowGameInput");
        Prefix(typeof(UnityEngine.EventSystems.EventSystem),"Update","AllowGameInput");
        Prefix(typeof(PlayerHealth),"Hurt","IgnoreDamage");
        Prefix(typeof(PlayerTumble),"TumbleSet","IgnoreFall");
        Prefix(typeof(Enemy),"SetChaseTarget","Chase");
        Prefix(typeof(EnemyVision),"VisionTrigger","See");
    }
    void Prefix(Type owner,string method,string handler)
    {
        patches.Patch(AccessTools.Method(owner,method),new HarmonyMethod(typeof(REPOrt),handler));
    }
    bool waitForRelease;
    CursorLockMode oldLock;
    bool oldVisible;
    static bool AllowGameInput() { return !mod || !(mod.open || mod.waitForRelease); }
    void SetOpen(bool value)
    {
        if (value) {
            oldLock = Cursor.lockState;
            oldVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        } else {
            Cursor.lockState = oldLock;
            Cursor.visible = oldVisible;
            waitForRelease = true;
        }
        open = value;
    }
    void OnDestroy() { if(open) SetOpen(false); foreach(var texture in palette) Destroy(texture); if (gun) gun.numberOfBullets = originalBullets; patches.UnpatchSelf(); mod = null; }
    static bool Local(Component c) { return PlayerAvatar.instance && c.GetComponentInParent<PlayerAvatar>() == PlayerAvatar.instance; }
    static bool IgnoreDamage(PlayerHealth __instance) { return !(mod.On("God Mode") && Local(__instance)); }
    static bool IgnoreFall(PlayerTumble __instance,bool __0) { return !(__0 && mod.On("No Tumble") && Local(__instance)); }
    static bool IgnoreTarget(PlayerAvatar p) { return p && p == PlayerAvatar.instance && mod.On("Non Enemy Targetable") && SemiFunc.IsMasterClientOrSingleplayer(); }
    static bool Chase(PlayerAvatar __0) { return !IgnoreTarget(__0); }
    static bool See(PlayerAvatar __1) { return !IgnoreTarget(__1); }
    ItemGun gun;
    int originalBullets;
    void SelfUpdate()
    {
        var player = PlayerAvatar.instance;
        if (player && PlayerController.instance && On(self[0])) PlayerController.instance.EnergyCurrent = PlayerController.instance.EnergyStart;
        var held = player && player.physGrabber ? grabbed.GetValue(player.physGrabber) as PhysGrabObject : null;
        var current = held && bullets.Value > 0 ? held.GetComponent<ItemGun>() : null;
        if (gun != current) {
            if (gun) gun.numberOfBullets = originalBullets;
            gun = current;
            if (gun) originalBullets = gun.numberOfBullets;
        }
        if (gun) gun.numberOfBullets = bullets.Value;
        if (held && On(self[3])) {
            var battery = held.GetComponent<ItemBattery>();
            if (battery && battery.batteryLife < 100f) battery.SetBatteryLife(100);
        }
        if (IgnoreTarget(player)) {
            player.EnemyVisionFreezeTimerSet(0.25f);
            player.OverrideDisableEnemyInvestigate(0.25f);
        }
    }
    void Update()
    {
        if (waitForRelease && !Enumerable.Range(0,5).Any(i => Input.GetMouseButton(i) || Input.GetMouseButtonUp(i))) waitForRelease=false;
        if (Input.GetKeyDown(KeyCode.F8)) SetOpen(!open);
        if (open && PlayerAvatar.instance) {
            SemiFunc.CursorUnlock(0.1f);
            SemiFunc.InputDisableAiming();
            SemiFunc.InputDisableMovement();
        }
        SelfUpdate();
        if ((open || On("Toggle ESP")) && Time.unscaledTime >= nextScan) {
            nextScan = Time.unscaledTime + 0.65f;
            enemyMeshes.Clear();
            for(int i=0;i<types.Length;i++) {
                objects[i].Clear();
                var seen = new HashSet<GameObject>();
                foreach(var type in types[i])
                    foreach(Component c in UnityEngine.Object.FindObjectsByType(type,FindObjectsSortMode.None))
                        if(seen.Add(c.gameObject)) {
                            objects[i].Add(c);
                            var enemy=c as Enemy;
                            if(enemy) {
                                var parent=enemy.GetComponentInParent<EnemyParent>();
                                enemyMeshes[enemy]=(parent ? parent.gameObject : enemy.gameObject).GetComponentsInChildren<Renderer>(true);
                            }
                        }
            }
        }
        if (frozen.Count>0 && Time.time >= nextFreeze) {
            nextFreeze = Time.time + 0.5f;
            frozen.RemoveWhere(e => !e || !e.gameObject.activeInHierarchy);
            if(SemiFunc.IsMasterClientOrSingleplayer()) foreach(var e in frozen) e.Freeze(0.7f);
            else frozen.Clear();
        }
    }
    void LateUpdate() { if(open) { Cursor.lockState=CursorLockMode.None; Cursor.visible=true; } if(PlayerAvatar.instance && Camera.main && fov.Value>0) Camera.main.fieldOfView = Mathf.Clamp(fov.Value,40,140); }
    string Caption(Component c) { var p=c as PlayerAvatar; return p ? SemiFunc.PlayerGetName(p) : c.name.Replace("(Clone)","").Trim(); }
    void OnGUI()
    {
        float s = Mathf.Min(scale.Value,Screen.width/544f,Screen.height/524f);
        var previous=GUI.matrix;
        try {
            GUI.matrix=Matrix4x4.identity;
            if(Event.current.type==EventType.Repaint && On("Toggle ESP")) DrawEsp();
            if(open) {
                GUI.matrix=Matrix4x4.Scale(new Vector3(s,s,1));
                InitTheme();
                window.x=Mathf.Clamp(window.x,0,Mathf.Max(0,Screen.width/s-window.width));
                window.y=Mathf.Clamp(window.y,0,Mathf.Max(0,Screen.height/s-window.height));
                window=GUILayout.Window(GetInstanceID(),window,Draw,"",panel);
                if(Event.current.isMouse || Event.current.type==EventType.ScrollWheel) Event.current.Use();
            }
        } finally { GUI.matrix=previous; }
    }
    readonly Dictionary<Enemy,Renderer[]> enemyMeshes=new Dictionary<Enemy,Renderer[]>();
    readonly Color[] markerColors={
        new Color32(89,205,255,255), new Color32(255,99,104,255), new Color32(255,211,92,255),
        new Color32(90,226,184,255), new Color32(188,151,255,255), new Color32(255,148,212,255),
        new Color32(255,166,83,255)
    };
    static readonly FieldInfo currentValue=Field(typeof(ValuableObject),"dollarValueCurrent"),
        valueReady=Field(typeof(ValuableObject),"dollarValueSet"), shop=Field(typeof(ItemAttributes),"shopItem"),
        shopPrice=Field(typeof(ItemAttributes),"value"), itemName=Field(typeof(ItemAttributes),"itemName");
    GUIStyle marker;
    string ItemLabel(Component c)
    {
        var item=c.GetComponent<ItemAttributes>();
        var valuable=c.GetComponent<ValuableObject>();
        string name=item ? itemName.GetValue(item) as string : null;
        if(string.IsNullOrEmpty(name)) name=Caption(c);
        int price;
        if(valuable && (bool)valueReady.GetValue(valuable)) price=Mathf.RoundToInt((float)currentValue.GetValue(valuable));
        else if(item && (bool)shop.GetValue(item)) price=(int)shopPrice.GetValue(item);
        else return name;
        return name+"\n$"+price.ToString("N0");
    }
    // Viewport coordinates stay normalized even when the scene is rendered to a smaller texture.
    static Vector2 ScreenPoint(Vector3 viewport,Rect area)
    {
        return new Vector2(area.x+viewport.x*area.width,area.y+(1f-viewport.y)*area.height);
    }
    bool EnemyRect(Enemy enemy,Camera cam,Rect area,out Rect box)
    {
        box=new Rect();
        Renderer[] meshes;
        if(!enemyMeshes.TryGetValue(enemy,out meshes)) return false;
        Bounds bounds=new Bounds();
        bool found=false;
        foreach(var r in meshes) {
            if(!r || !r.enabled || !r.gameObject.activeInHierarchy || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if(found) bounds.Encapsulate(r.bounds); else bounds=r.bounds;
            found=true;
        }
        if(!found || bounds.Contains(cam.transform.position)) return false;
        var low=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
        var high=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
        for(int i=0;i<8;i++) {
            Vector3 corner=new Vector3((i&1)==0?bounds.min.x:bounds.max.x,(i&2)==0?bounds.min.y:bounds.max.y,(i&4)==0?bounds.min.z:bounds.max.z);
            Vector3 viewport=cam.WorldToViewportPoint(corner);
            if(viewport.z<cam.nearClipPlane) return false;
            Vector2 p=ScreenPoint(viewport,area);
            low=Vector2.Min(low,p);
            high=Vector2.Max(high,p);
        }
        low=Vector2.Max(low,area.min);
        high=Vector2.Min(high,area.max);
        box=Rect.MinMaxRect(low.x,low.y,high.x,high.y);
        return box.width>=2 && box.height>=2;
    }
    static void Paint(Rect rect,Color color)
    {
        GUI.color=color;
        GUI.DrawTexture(rect,Texture2D.whiteTexture);
    }
    void DrawEsp()
    {
        var cam=GameDirector.instance ? SemiFunc.MainCamera() : Camera.main;
        if(!cam) return;
        Rect v=cam.targetTexture ? new Rect(0,0,1,1) : cam.rect;
        Rect area=new Rect(v.x*Screen.width,(1-v.yMax)*Screen.height,v.width*Screen.width,v.height*Screen.height);
        if(marker==null) marker=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold,padding=new RectOffset()};
        marker.fontSize=Mathf.RoundToInt(14*scale.Value);
        Color previous=GUI.color;
        try {
            for(int i=0;i<kinds.Length;i++) {
                if(!On(kinds[i]+" ESP")) continue;
                Color color=markerColors[i];
                foreach(var c in objects[i]) {
                    if(!c || !c.gameObject.activeInHierarchy || c==PlayerAvatar.instance) continue;
                    var enemy=c as Enemy;
                    Vector3 anchor=enemy && enemy.CenterTransform ? enemy.CenterTransform.position : c.transform.position;
                    Vector3 viewport=cam.WorldToViewportPoint(anchor);
                    if(viewport.z<cam.nearClipPlane) continue;
                    Vector2 point=ScreenPoint(viewport,area);
                    Rect box;
                    if(enemy && EnemyRect(enemy,cam,area,out box)) {
                        Paint(new Rect(box.x,box.y,box.width,2),color);
                        Paint(new Rect(box.x,box.yMax-2,box.width,2),color);
                        Paint(new Rect(box.x,box.y,2,box.height),color);
                        Paint(new Rect(box.xMax-2,box.y,2,box.height),color);
                        point=new Vector2(box.center.x,box.yMin);
                    } else if(!area.Contains(point)) continue;
                    string label=i==2 ? ItemLabel(c) : Caption(c);
                    var size=marker.CalcSize(new GUIContent(label))+new Vector2(10,4);
                    Rect badge=new Rect(Mathf.Clamp(point.x-size.x/2,area.xMin,area.xMax-size.x),
                        Mathf.Max(area.yMin,point.y-size.y-3),size.x,size.y);
                    Paint(badge,new Color(0.035f,0.05f,0.065f,0.85f));
                    GUI.color=Color.white;
                    marker.normal.textColor=color;
                    GUI.Label(badge,label,marker);
                }
            }
        } finally { GUI.color=previous; }
    }
    readonly List<Texture2D> palette = new List<Texture2D>();
    GUIStyle panel, action, selectedAction, title;
    readonly string[] tabs = {"Settings","Self Cheats","Visuals","Player","Enemy"};

    Texture2D Fill(byte r,byte g,byte b)
    {
        var texture=new Texture2D(1,1);
        texture.SetPixel(0,0,new Color32(r,g,b,255));
        texture.Apply();
        palette.Add(texture);
        return texture;
    }
    void InitTheme()
    {
        if(panel!=null) return;
        panel=new GUIStyle(GUI.skin.box);
        panel.normal.background=Fill(20,29,38);
        panel.padding=new RectOffset(16,16,12,12);
        action=new GUIStyle(GUI.skin.button);
        action.fixedHeight=30;
        action.border=new RectOffset();
        action.normal.background=Fill(44,60,73);
        action.hover.background=Fill(62,83,96);
        action.active.background=Fill(35,111,106);
        action.normal.textColor=action.hover.textColor=action.active.textColor=Color.white;
        selectedAction=new GUIStyle(action);
        selectedAction.normal.background=Fill(29,103,99);
        title=new GUIStyle(GUI.skin.label);
        title.fontSize=24;
        title.fontStyle=FontStyle.Bold;
        title.normal.textColor=new Color32(118,230,202,255);
    }
    void Toggle(string key)
    {
        bool enabled=On(key);
        if(GUILayout.Button((enabled?"●   ":"○   ")+key,enabled?selectedAction:action))
            flags[key].Value=!enabled;
    }
    bool Button(string text) { return GUILayout.Button(text,action); }
    void Draw(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("REPOrt",title);
        GUILayout.FlexibleSpace();
        if(GUILayout.Button("Fermer  ·  F8",action,GUILayout.Width(110))) SetOpen(false);
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        for(int i=0;i<tabs.Length;i++) {
            if(GUILayout.Button(tabs[i],i==tab?selectedAction:action) && i!=tab) {
                tab=i;
                scroll=Vector2.zero;
                status="";
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(10);
        scroll=GUILayout.BeginScrollView(scroll);
        try {
            if(tab==0) {
                if(Button("Reset Settings")) { foreach(var pair in Config) pair.Value.BoxedValue=pair.Value.DefaultValue; Config.Save(); status="Settings reset"; }
                if(Button("Save Settings")) { Config.Save(); status="Settings saved"; }
                if(Button("Reload Settings")) { Config.Reload(); status="Settings reloaded"; }
                Toggle("Debug Mode");
                GUILayout.Label("GUI Size   "+scale.Value.ToString("0.00"));
                scale.Value=GUILayout.HorizontalSlider(scale.Value,0.6f,2f);
            } else if(tab==1) {
                foreach(var key in self) Toggle(key);
                Slider(bullets,"Gun Bullet Amount",32);
            } else if(tab==2) {
                Slider(fov,"FOV",140);
                Toggle("Toggle ESP");
                if(Button("Toggle All ESP")) {
                    bool value=kinds.Any(k=>!On(k+" ESP"));
                    foreach(var k in kinds) flags[k+" ESP"].Value=value;
                }
                foreach(var k in kinds) Toggle(k+" ESP");
            } else if(PlayerAvatar.instance) {
                bool host=SemiFunc.IsMasterClientOrSingleplayer();
                if(!host) GUILayout.Label("Les commandes de partie nécessitent l’hôte.");
                if(tab==3) Players(host); else Enemies(host);
            }
        } catch(Exception ex) {
            if(ex is ExitGUIException) throw;
            status=ex.Message;
            if(On("Debug Mode")) Logger.LogError(ex);
        } finally { GUI.enabled=true; GUILayout.EndScrollView(); }
        GUILayout.Label(status);
        GUI.DragWindow(new Rect(0,0,350,45));
    }
    void Slider(ConfigEntry<int> entry,string label,int max)
    {
        GUILayout.Label(label+"   "+(entry.Value==0?"Game default":entry.Value.ToString()));
        int value=Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value,0,max));
        entry.Value=entry==fov && value>0 && value<40 ? (value<entry.Value?0:40) : value;
    }
    void Players(bool host)
    {
        GUILayout.Label("Damage Amount");
        damage=GUILayout.TextField(damage,9);
        int amount;
        bool valid=int.TryParse(damage,out amount) && amount>0;
        foreach(PlayerAvatar p in objects[0]) {
            if(!p || !p.playerHealth) continue;
            var h=p.playerHealth;
            int cur=(int)hp.GetValue(h), max=(int)maximum.GetValue(h);
            GUILayout.Label(Caption(p)+"   "+cur+" / "+max+" HP");
            GUI.enabled=host && cur<max;
            if(Button("Heal")) h.HealOther(max-cur,true);
            GUI.enabled=host && valid;
            if(Button("Damage")) h.HurtOther(amount,p.transform.position,false);
            GUI.enabled=true;
        }
    }
    void Kill(Enemy e) { var h=enemyHealth.GetValue(e) as EnemyHealth; if(h) h.Hurt(Math.Max(1,(int)enemyHp.GetValue(h)),Vector3.zero); }
    void Enemies(bool host)
    {
        var enemies=objects[1].OfType<Enemy>().Where(e=>e && e.gameObject.activeInHierarchy).ToArray();
        GUILayout.Label("Enemies   "+enemies.Length);
        GUI.enabled=host;
        if(Button("Kill All")) foreach(var e in enemies) Kill(e);
        foreach(var e in enemies) {
            var h=enemyHealth.GetValue(e) as EnemyHealth;
            var p=target.GetValue(e) as PlayerAvatar;
            GUI.enabled=true;
            GUILayout.Label(Caption(e)+"   "+(h?enemyHp.GetValue(h)+" / "+h.health+" HP":"—")+"   Target: "+(p?Caption(p):"—"));
            GUI.enabled=host && h;
            if(Button("Kill")) Kill(e);
            GUI.enabled=host;
            if(Button(frozen.Contains(e)?"Unfreeze":"Freeze") && !frozen.Remove(e)) frozen.Add(e);
        }
        GUI.enabled=true;
    }
}
