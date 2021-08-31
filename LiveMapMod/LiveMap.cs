using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using ModAPI.Attributes;
using TheForest.Items;
using TheForest.Items.World;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace LiveMap
{

    class LiveMap : MonoBehaviour
    {

        [ExecuteOnGameStart]
        private static void AddMeToScene()
        {
            new GameObject("__LiveMap__").AddComponent<LiveMap>();
        }

        const int TYPE_PLAYER = 0;
        const int TYPE_ENEMY = 1;
        const int TYPE_CAVE_ENTRANCE = 2;
        const int TYPE_PICKUP = 3;

        const int ACTION_TYPE_CLEAR = 0;

        const float FRAME_RATE = 1f; // 5 per second (limited by coroutine SendUDP, I think)

        private float lastUpdate = 0.0f;

        void Awake()
        {
            ModAPI.Log.Write("live map started: 15:39");
        }

        void Update()
        {
            if (ModAPI.Input.GetButtonDown("Update"))
            {
                FindStaticObjects();
            }

            lastUpdate += Time.deltaTime;
            if (lastUpdate > FRAME_RATE)
            {
                lastUpdate = 0.0f;

                List<MapEntry> entries = new List<MapEntry>();
                try
                {
                    if (LocalPlayer.Transform != null && LocalPlayer.Transform.position != null) 
                    {
                        entries.Add(new MapEntry(TYPE_PLAYER, LocalPlayer.GameObject.GetInstanceID(), -1, "player", LocalPlayer.Transform.position, LocalPlayer.Transform.rotation.eulerAngles, LocalPlayer.IsInCaves));
                    }

                    FindCanibals(entries);
                }
                catch (Exception e)
                {
                    ModAPI.Log.Write("Error: " + e);
                }

                StartCoroutine(SendUDP(entries, false));
            }
        }

        private void FindCanibals(List<MapEntry> entries)
        {
            if (Scene.MutantControler == null || Scene.MutantControler.ActiveWorldCannibals == null)
            {
                return;
            }

            HashSet<int> cannibalIDs = new HashSet<int>();

            if (LocalPlayer.IsInCaves)
            {
                foreach (var c in Scene.MutantControler.ActiveCaveCannibals)
                {
                    entries.Add(new MapEntry(TYPE_ENEMY, c));
                    cannibalIDs.Add(c.GetInstanceID());
                }
            }
            else
            {
                foreach (var c in Scene.MutantControler.ActiveWorldCannibals)
                {
                    entries.Add(new MapEntry(TYPE_ENEMY, c));
                    cannibalIDs.Add(c.GetInstanceID());
                }
            }

            foreach (GameObject c in Scene.MutantControler.activeInstantSpawnedCannibals)
            {
                if (!cannibalIDs.Contains(c.GetInstanceID()))
                {
                    entries.Add(new MapEntry(TYPE_ENEMY, c));
                    cannibalIDs.Add(c.GetInstanceID());
                }
            }
        }

        private void FindCaveEntrances(List<MapEntry> entries)
        {
            if (Scene.SceneTracker == null)
            {
                return;
            }

            foreach (var ce in Scene.SceneTracker.caveEntrances) {
                GameObject go = ce.blackBackingGo;
                if (go != null)
                {
                    entries.Add(new MapEntry(TYPE_CAVE_ENTRANCE, go));
                    continue;
                }
                go = ce.fadeToDarkGo;
                if (go != null)
                {
                    entries.Add(new MapEntry(TYPE_CAVE_ENTRANCE, go));
                    continue;
                }
                go = ce.blackBackingFadeGo;
                if (go != null)
                {
                    entries.Add(new MapEntry(TYPE_CAVE_ENTRANCE, go));
                    continue;
                }
                // ModAPI.Log.Write("Cave entrance: " + go.transform.position);
            }
        }

        private void FindStaticObjects()
        {
            List<MapEntry> entries = new List<MapEntry>();
            FindItems(entries, "PickUp", TYPE_PICKUP);
            FindCaveEntrances(entries);
            StartCoroutine(SendUDP(entries, true));
        }

        private void FindItems(List<MapEntry> entries, string itemName, int entryType)
        {
            GameObject[] objectsOfType = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (objectsOfType.NullOrEmpty())
                return;

            // StringComparison comparisonType = StringComparison.InvariantCultureIgnoreCase;
            // CultureInfo culture = CultureInfo.InvariantCulture;

            foreach (GameObject gameObject in objectsOfType)
            {
                // if (!((UnityEngine.Object)gameObject == (UnityEngine.Object)null) && !gameObject.name.NullOrEmpty() && gameObject.name.Equals(name, comparisonType))
                // if (!((UnityEngine.Object)gameObject == (UnityEngine.Object)null) && !gameObject.name.NullOrEmpty() && culture.CompareInfo.IndexOf(gameObject.name, itemName, CompareOptions.IgnoreCase) >= 0)
                if (IsItemPickUp(gameObject))
                {
                    // ModAPI.Log.Write("Found: " + gameObject.name + ", " + gameObject.GetInstanceID() + ", " + gameObject.transform.position);
                    entries.Add(new MapEntry(entryType, gameObject));
                    //DumpGameObject(gameObject);
                }
            }

            int itemId = 54; // rope
            Item item = ItemDatabase.ItemById(itemId);
            if (item != null)
            {
                ModAPI.Log.Write("Found item: " + item._name);
                GameObject itemGO = item._pickupPrefab.gameObject;
                // DumpGameObject(itemGO);
            }
        }

        private bool IsItemPickUp(GameObject go)
        {
            if ((UnityEngine.Object) go == (UnityEngine.Object) null)
            {
                return false;
            }
            if (go.name.NullOrEmpty())
            {
                return false;
            }

            PickUp pickUp = go.GetComponentInChildren(typeof(PickUp)) as PickUp;
            return pickUp != null;
        }

        private void DumpGameObject(GameObject go)
        {
            ModAPI.Log.Write("Game object: " + go.GetInstanceID() + ", " + go.name + " [" + go.tag + "]:" + go.ToString());
            foreach (Component c in go.GetComponentsInChildren<Component>())
            {
                ModAPI.Log.Write("\tComponent: " + c);
                if (c is PickUp)
                {
                    PickUp pickUp = c as PickUp;
                    ModAPI.Log.Write("\tPickUp Item ID: " + pickUp._itemId);
                }
            }
        }

        IEnumerator SendUDP(List<MapEntry> entries, bool clearStaticObjects)
        {
            if (entries.Count == 0)
            {
                yield return null;
            }

            using (UdpClient udp = new UdpClient())
            {
                if (clearStaticObjects)
                {
                    SendUDP(udp, new Action(ACTION_TYPE_CLEAR));
                }

                foreach (MapEntry e in entries)
                {
                    SendUDP(udp, e);
                }
            }   
        }

        private void SendUDP(UdpClient udp, System.Object obj)
        {
            string json = JsonUtility.ToJson(obj);
            Byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            udp.Send(jsonBytes, jsonBytes.Length, "127.0.0.1", 9999);
        }

    }

    [Serializable]
    public class MapEntry
    {
        public int type;
        public int id;
        public int itemID;
        public string name;
        public float x;
        public float y;
        public float z;
        public float rotZ;
        public bool inCave;

        public MapEntry(int type, GameObject gameObject) : 
            this(type, gameObject.GetInstanceID(), GetItemID(gameObject), gameObject.name, gameObject.transform.position, gameObject.transform.rotation.eulerAngles, false)
        { }

        public MapEntry(int type, int id, int itemID, string name, Vector3 position, Vector3? rotation = null, bool? inCave = false)
        {
            this.type = type;
            this.id = id;
            this.itemID = itemID;
            this.name = name;
            this.x = position.x;
            this.y = position.z; // y => z
            this.z = position.y; // z => y
            this.rotZ = rotation != null ? rotation.Value.y : 0; // y is z
            this.inCave = inCave != null && inCave.Value;
        }

        private static int GetItemID(GameObject gameObject)
        {
            PickUp pickUp = gameObject.GetComponentInChildren(typeof(PickUp)) as PickUp;
            if (pickUp != null)
            {
                return pickUp._itemId;
            }
            return -1;
        }

    }

    [Serializable]
    public class Action
    {
        public int actionType;

        public Action(int actionType)
        {
            this.actionType = actionType;
        }

    }

}
