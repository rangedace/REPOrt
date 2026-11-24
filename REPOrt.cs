using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("fr.rangedace.report", "REPOrt", "1.0.0")]
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
    Rect window = new Rect(18,18,440,480);
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
        Prefix(typeof(PlayerHealth),"Hurt","IgnoreDamage");
        Prefix(typeof(PlayerTumble),"TumbleSet","IgnoreFall");
        Prefix(typeof(Enemy),"SetChaseTarget","Chase");
        Prefix(typeof(EnemyVision),"VisionTrigger","See");
    }
    void Prefix(Type owner,string method,string handler)
    {
        patches.Patch(AccessTools.Method(owner,method),new HarmonyMethod(typeof(REPOrt),handler));
    }
    void OnDestroy() { if (gun) gun.numberOfBullets = originalBullets; patches.UnpatchSelf(); mod = null; }
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
        if (Input.GetKeyDown(KeyCode.F8)) open = !open;
        if (open && PlayerAvatar.instance) {
            SemiFunc.CursorUnlock(0.1f);
            SemiFunc.InputDisableAiming();
            SemiFunc.InputDisableMovement();
        }
        SelfUpdate();
        if ((open || On("Toggle ESP")) && Time.unscaledTime >= nextScan) {
            nextScan = Time.unscaledTime + 0.65f;
            for(int i=0;i<types.Length;i++) {
                objects[i].Clear();
                var seen = new HashSet<GameObject>();
                foreach(var type in types[i])
                    foreach(Component c in UnityEngine.Object.FindObjectsByType(type,FindObjectsSortMode.None))
                        if(seen.Add(c.gameObject)) objects[i].Add(c);
            }
        }
        if (frozen.Count>0 && Time.time >= nextFreeze) {
            nextFreeze = Time.time + 0.5f;
            frozen.RemoveWhere(e => !e || !e.gameObject.activeInHierarchy);
            if(SemiFunc.IsMasterClientOrSingleplayer()) foreach(var e in frozen) e.Freeze(0.7f);
            else frozen.Clear();
        }
    }
    void LateUpdate() { if(PlayerAvatar.instance && Camera.main && fov.Value>0) Camera.main.fieldOfView = Mathf.Clamp(fov.Value,40,140); }
    string Caption(Component c) { var p=c as PlayerAvatar; return p ? SemiFunc.PlayerGetName(p) : c.name.Replace("(Clone)","").Trim(); }
    void OnGUI()
    {
        float s = Mathf.Min(scale.Value,Screen.width/450f,Screen.height/500f);
        var previous=GUI.matrix;
        GUI.matrix=Matrix4x4.Scale(new Vector3(s,s,1));
        try {
            var cam=Camera.main;
            if(Event.current.type==EventType.Repaint && cam && On("Toggle ESP")) {
                for(int i=0;i<kinds.Length;i++) if(On(kinds[i]+" ESP"))
                    foreach(var c in objects[i]) {
                        if(!c || c==PlayerAvatar.instance) continue;
                        var pos=cam.WorldToScreenPoint(c.transform.position);
                        if(pos.z<=0) continue;
                        GUI.Label(new Rect(pos.x/s,(Screen.height-pos.y)/s,280,24),Caption(c));
                    }
            }
            if(open) window=GUILayout.Window(GetInstanceID(),window,Draw,"REPOrt   ·   F8");
        } finally { GUI.matrix=previous; }
    }
    void Toggle(string key) { flags[key].Value=GUILayout.Toggle(On(key),key); }
    bool Button(string text) { return GUILayout.Button(text); }
    void Draw(int id)
    {
        int selected=GUILayout.Toolbar(tab,new[]{"Settings","Self Cheats","Visuals","Player","Enemy"});
        if(selected!=tab) { tab=selected; scroll=Vector2.zero; status=""; }
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
        GUI.DragWindow(new Rect(0,0,440,20));
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
