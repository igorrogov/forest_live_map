using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ModAPI.Attributes;
using TheForest.Items;
using TheForest.Items.World;
using TheForest.Utils;
using UnityEngine;

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

        const float UPDATE_INTERVAL_SECONDS = 1f;

        private float lastUpdate = 0.0f;

        private TcpClient client;

        private Queue<System.Object> queue = new Queue<System.Object>();
        private readonly object queueLock = new object();
        private bool isThreadStarted = false;

        void Awake()
        {
            ModAPI.Log.Write("live map started: 11:35");
            if (!isThreadStarted)
            {
                var th = new Thread(TcpClientLoop);
                th.Start();
                isThreadStarted = true;
                ModAPI.Log.Write("Started TCP client thread");
            }
        }

        void Update()
        {
            if (ModAPI.Input.GetButtonDown("Update"))
            {
                FindStaticObjects();
            }

            lastUpdate += Time.deltaTime;
            if (lastUpdate > UPDATE_INTERVAL_SECONDS)
            {
                lastUpdate = 0.0f;

                try
                {
                    if (LocalPlayer.Transform != null && LocalPlayer.Transform.position != null) 
                    {
                        AddObjectToQueue(new MapEntry(TYPE_PLAYER, LocalPlayer.GameObject.GetInstanceID(), -1, "player", LocalPlayer.Transform.position, LocalPlayer.Transform.rotation.eulerAngles, LocalPlayer.IsInCaves));
                    }

                    FindCannibals();
                }
                catch (Exception e)
                {
                    ModAPI.Log.Write("Error: " + e);
                }
            }
        }

        private void FindCannibals()
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
                    AddObjectToQueue(new MapEntry(TYPE_ENEMY, c));
                    cannibalIDs.Add(c.GetInstanceID());
                }
            }
            else
            {
                foreach (var c in Scene.MutantControler.ActiveWorldCannibals)
                {
                    AddObjectToQueue(new MapEntry(TYPE_ENEMY, c));
                    cannibalIDs.Add(c.GetInstanceID());
                }
            }

            foreach (GameObject c in Scene.MutantControler.activeInstantSpawnedCannibals)
            {
                if (!cannibalIDs.Contains(c.GetInstanceID()))
                {
                    AddObjectToQueue(new MapEntry(TYPE_ENEMY, c));
                    cannibalIDs.Add(c.GetInstanceID());
                }
            }
        }

        private void FindStaticObjects()
        {
            List<MapEntry> entries = new List<MapEntry>();
            FindItems(entries, "PickUp", TYPE_PICKUP);
            //FindSpawnPools();
            FindCaveEntrances(entries);
            // ModAPI.Log.Write("Sending static entries: " + entries.Count);
            SendStaticObjects(entries);
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
            }
        }

        private void FindItems(List<MapEntry> entries, string itemName, int entryType)
        {
            GameObject[] objectsOfType;

            objectsOfType = UnityEngine.Object.FindObjectsOfType<GameObject>();
            ModAPI.Log.Write("Object.FindObjectsOfType: " + objectsOfType.Length);
            HandleItems(entries, entryType, objectsOfType);

            objectsOfType = Resources.FindObjectsOfTypeAll(typeof(GameObject)) as GameObject[];
            ModAPI.Log.Write("Resources.FindObjectsOfTypeAll: " + objectsOfType.Length);
            HandleItems(entries, entryType, objectsOfType);
        }

        private void HandleItems(List<MapEntry> entries, int entryType, GameObject[] objectsOfType)
        {
            if (objectsOfType.NullOrEmpty())
                return;

            // StringComparison comparisonType = StringComparison.InvariantCultureIgnoreCase;
            // CultureInfo culture = CultureInfo.InvariantCulture;

            foreach (GameObject gameObject in objectsOfType)
            {
                // if (!((UnityEngine.Object)gameObject == (UnityEngine.Object)null) && !gameObject.name.NullOrEmpty() && gameObject.name.Equals(name, comparisonType))
                // if (!((UnityEngine.Object)gameObject == (UnityEngine.Object)null) && !gameObject.name.NullOrEmpty() && culture.CompareInfo.IndexOf(gameObject.name, itemName, CompareOptions.IgnoreCase) >= 0)
                if (ContainsComponent(gameObject, typeof(PickUp)))
                {
                    // ModAPI.Log.Write("Found: " + gameObject.name + ", " + gameObject.GetInstanceID() + ", " + gameObject.transform.position);
                    entries.Add(new MapEntry(entryType, gameObject));
                    //DumpGameObject(gameObject);
                }
            }

            //int itemId = 54; // rope
            //Item item = ItemDatabase.ItemById(itemId);
            //if (item != null)
            //{
            //    ModAPI.Log.Write("Found item: " + item._name);
            //    GameObject itemGO = item._pickupPrefab.gameObject;
            //    // DumpGameObject(itemGO);
            //}
        }

        private void FindSpawnPools()
        {
            MonoBehaviour[] objectsOfType = Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour)) as MonoBehaviour[];
            if (objectsOfType.NullOrEmpty())
                return;

            CultureInfo culture = CultureInfo.InvariantCulture;
            string itemName = "rope";

            foreach (MonoBehaviour obj in objectsOfType)
            {
                if (!((UnityEngine.Object)obj == (UnityEngine.Object)null) && !obj.name.NullOrEmpty() && culture.CompareInfo.IndexOf(gameObject.name, itemName, CompareOptions.IgnoreCase) >= 0)
                {
                    ModAPI.Log.Write("Found SpawnItemFromPool: " + obj.name + ", " + obj.GetInstanceID() + ", " + obj.transform.position + ", " + obj);
                    // entries.Add(new MapEntry(entryType, gameObject));
                    DumpGameObject(obj.transform.gameObject);
                }
            }
        }

        private bool ContainsComponent(GameObject go, Type type)
        {
            if ((UnityEngine.Object)go == (UnityEngine.Object)null)
            {
                return false;
            }
            if (go.name.NullOrEmpty())
            {
                return false;
            }

            Component component = go.GetComponentInChildren(type);
            return component != null;
        }

        private void DumpGameObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }

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

        private void SendStaticObjects(List<MapEntry> entries)
        {
            try
            {
                ModAPI.Log.Write("SendStaticObjects: " + entries.Count);

                int nrPickup = 0;
                int nrCave = 0;

                AddObjectToQueue(new Action(ACTION_TYPE_CLEAR));

                foreach (MapEntry e in entries)
                {
                    AddObjectToQueue(e);

                    if (e.type == TYPE_PICKUP)
                    {
                        nrPickup++;
                    }
                    if (e.type == TYPE_CAVE_ENTRANCE)
                    {
                        nrCave++;
                    }
                }
                ModAPI.Log.Write("Sent: " + nrCave + " caves, " + nrPickup + " pickups");
            }
            catch (Exception e)
            {
                ModAPI.Log.Write("Error: " + e);
            }
        }

        private void AddObjectToQueue(System.Object obj)
        {
            lock (queueLock)
            {
                queue.Enqueue(obj);
            }
        }

        private void TcpClientLoop()
        {
            try
            {
                while (true)
                {
                    System.Object obj = null;
                    lock (queueLock)
                    {
                        if (queue.Count > 0)
                        {
                            obj = queue.Dequeue();
                        }
                    }

                    if (obj == null)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    SendAsJson(obj);
                }
            }
            catch (Exception e)
            {
                ModAPI.Log.Write("Error: " + e);
            }
        }

        private void SendAsJson(System.Object obj)
        {
            string json = JsonUtility.ToJson(obj);
            json += "\n"; // separate objects with new line
            Byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            TcpClient client = GetTcpClient();
            NetworkStream stream = client.GetStream();
            // using (NetworkStream stream = client.GetStream())
            // {
            stream.Write(jsonBytes, 0, jsonBytes.Length);
            // }
            // ModAPI.Log.Write("Sent: " + json);
        }

        private TcpClient GetTcpClient()
        {
            if (client != null && client.Connected)
            {
                return client;
            }
            if (client != null)
            {
                client.Close();
            }

            client = new TcpClient("127.0.0.1", 9999);
            ModAPI.Log.Write("Connected to 127.0.0.1:9999");
            return client;
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
