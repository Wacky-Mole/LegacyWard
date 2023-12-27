using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using fastJSON;
using HarmonyLib;
using KeyManager;
using PieceManager;
using ServerSync;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace LegacyWard
{
    [BepInPlugin(ModGUID, ModName, VERSION)]
    [KeyManager.VerifyKey(Author +"/" + ModName, LicenseMode.DedicatedServer)]
    public class Wackyward : BaseUnityPlugin
    {
        internal const string ModName = "LegacyWard";
        internal const string VERSION = "1.0.7";
        internal const string Author = "WackyMole";
        internal const string ModGUID = Author + "." + ModName;
        private static AssetBundle asset;
        private static AssetBundle asset_vfx;
        private static GameObject Ward_Prefab;
        private static GameObject Ward_Prefab_par;
        private static GameObject FlashShield;
        private static GameObject FlashShield_Permit;
        private static GameObject FlashShield_Fuel;
        private static GameObject FlashShield_Activate;
        private static GameObject FlashShield_Deactivate;
        private static ConfigEntry<string> WardRecipe;
        private static ConfigEntry<int> WardDefaultRadius;
        private static ConfigEntry<string> WardRadiusPrefabs;
        private static ConfigEntry<string> WardFuelPrefabs;
        private static ConfigEntry<int> WardMaxFuel;
        private static bool isServer => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
        private readonly ConfigSync configSync = new(ModGUID) { DisplayName = ModName };
        private readonly Harmony _harmony = new(ModGUID);

        private static bool _canPlaceWard;


        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);
            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true) =>
            config(group, name, value, new ConfigDescription(description), synchronizedSetting);

        private void Awake()
        {
            _thistype = this;
            configSync.ModRequired = true;
            asset = GetAssetBundle("wackyward");
            asset_vfx = GetAssetBundle("wackywardvfx");
            WardMaxFuel = config("Ward", "WardMaxFuel", 60 * 60 * 24 * 7*3, "Ward Max Fuel - 3 Weeks or 21 days");
            WardDefaultRadius = config("Ward", "WardDefaultRadius", 30, "Ward Default Radius");
            WardRadiusPrefabs = config("Ward", "WardRadiusPrefabs", "Wood, 20", "Ward Radius Prefabs");
            WardFuelPrefabs = config("Ward", "WardFuelPrefabs", "Stone, 100", "Ward Fuel Prefabs");
            WardRecipe = config("Ward", "WardRecipe", "SwordCheat,1", "Ward Recipe");
            WardRecipe.SettingChanged += ResetRecipe;

            JSON.Parameters = new JSONParameters
            {
                UseExtensions = false,
                SerializeNullValues = false,
                DateTimeMilliseconds = false,
                UseUTCDateTime = true,
                UseOptimizedDatasetSchema = true,
                UseValuesOfEnums = true,
            };

            Ward_Prefab = asset.LoadAsset<GameObject>("WackyWard");
            
            //BuildPiece Ward_PrefabBP = new BuildPiece("wackyward", "WackyWard");
            //Ward_Prefab = Ward_PrefabBP.Prefab;
            Ward_Prefab.AddComponent<WackyWard_Component>();
            Ward_Prefab.GetComponent<Piece>().m_name = "Legacy Ward";
            Ward_Prefab.GetComponent<Piece>().m_description = "Legacy Ward sets in the Center";
           // Ward_PrefabBP.Name.English("Legacy Ward");
            //Ward_PrefabBP.Description.English("Legacy Ward sets in the Center");


            Ward_Prefab_par = asset.LoadAsset<GameObject>("WackyWardEdge");
           // BuildPiece Ward_Prefab_parBP = new BuildPiece("wackyward", "WackyWardEdge");
           // Ward_Prefab_par = Ward_Prefab_parBP.Prefab;
            Ward_Prefab_par.AddComponent<WackyWard_Component>();
            Ward_Prefab_par.GetComponent<Piece>().m_name = "Legacy Ward Edge";
            Ward_Prefab_par.GetComponent<Piece>().m_description = "Legacy Ward Sets on the Parameter";
            //Ward_Prefab_parBP.Name.English("Legacy Ward Edge");
            //Ward_Prefab_parBP.Description.English("Legascy Ward Sets on the Parameter");

            
            FlashShield = asset_vfx.LoadAsset<GameObject>("WackyWard_Explosion");
            FlashShield_Permit = asset_vfx.LoadAsset<GameObject>("WackyWard_Permit");
            FlashShield_Fuel = asset_vfx.LoadAsset<GameObject>("WackyWard_Fuel");
            FlashShield_Activate = asset_vfx.LoadAsset<GameObject>("WackyWard_Activate");
            FlashShield_Deactivate = asset_vfx.LoadAsset<GameObject>("WackyWard_Deactivate");
            if (isServer) ServerSideInit();

            _harmony.PatchAll();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece), typeof(Piece))]
        static class PlacePiece_Patch
        {
            static bool Prefix(Piece piece)
            {
                if (!piece.GetComponent<WackyWard_Component>()) return true;
                if (!WackyWard_Component.CanBuild(Player.m_localPlayer.m_placementGhost.transform.position))
                {
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Another ward nearby");
                    return false;
                }

                if (!_canPlaceWard && !Player.m_debugMode)
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                        "<color=red>Ward Limit</color>");
                    return false;
                }

                return true;
            }
        }


        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
        public static class FejdStartupPatch
        {
            static void Postfix(FejdStartup __instance)
            {
                if (isServer)
                    return;


                if (ZNet.m_onlineBackend == OnlineBackendType.PlayFab)
                {

                    _thistype._harmony.Patch(AccessTools.DeclaredMethod(typeof(ZPlayFabMatchmaking), nameof(ZPlayFabMatchmaking.CreateLobby)),
                         postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(FejdStartupPatch),
                             nameof(gamepassServer))));

                }
                else if (ZNet.m_onlineBackend == OnlineBackendType.Steamworks)
                {
                    _thistype._harmony.Patch(AccessTools.DeclaredMethod(typeof(ZSteamMatchmaking), nameof(ZSteamMatchmaking.RegisterServer)),
                        postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(FejdStartupPatch),
                            nameof(steamServer))));
                }

            }

            private static void steamServer()
            {
                _thistype.Logger.LogError("Steam Lobby is active");
                Application.Quit();

            }

            private static void gamepassServer()
            {
                _thistype.Logger.LogError("Zplay Lobby is active");
                Application.Quit();
            }

        }


        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static class ZNetScene_Awake_Patch
        {
            private static void Postfix(ZNetScene __instance)
            {
                if (KeyManager.KeyManager.CheckAllowed() == State.Verified)
                { }
                else
                {
                    Application.Quit();
                }

                if (isServer)
                {
                    ZRoutedRpc.instance.Register("WackyWard wardplaced", WardPlaced);
                    return;
                }

                ZRoutedRpc.instance.Register("WackyWard Data", new Action<long, bool>(ReceiveData_WackyWard));

                List<GameObject> hammer = __instance.GetPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared
                    .m_buildPieces
                    .m_pieces;


                if (!hammer.Contains(Ward_Prefab)) hammer.Add(Ward_Prefab);
                if (!hammer.Contains(Ward_Prefab_par)) hammer.Add(Ward_Prefab_par);

                __instance.m_prefabs.Add(FlashShield);
                __instance.m_namedPrefabs.Add(FlashShield.name.GetStableHashCode(), FlashShield);
                __instance.m_prefabs.Add(FlashShield_Permit);
                __instance.m_namedPrefabs.Add(FlashShield_Permit.name.GetStableHashCode(), FlashShield_Permit);
                __instance.m_prefabs.Add(FlashShield_Fuel);
                __instance.m_namedPrefabs.Add(FlashShield_Fuel.name.GetStableHashCode(), FlashShield_Fuel);
                __instance.m_prefabs.Add(FlashShield_Activate);
                __instance.m_namedPrefabs.Add(FlashShield_Activate.name.GetStableHashCode(), FlashShield_Activate);
                __instance.m_prefabs.Add(FlashShield_Deactivate);
                __instance.m_namedPrefabs.Add(FlashShield_Deactivate.name.GetStableHashCode(), FlashShield_Deactivate);
                __instance.m_prefabs.Add(Ward_Prefab);
                __instance.m_namedPrefabs.Add(Ward_Prefab.name.GetStableHashCode(), Ward_Prefab);

                __instance.m_prefabs.Add(Ward_Prefab_par);
                __instance.m_namedPrefabs.Add(Ward_Prefab_par.name.GetStableHashCode(), Ward_Prefab_par);
                
            }

            private static void WardPlaced(long sender)
            {
                ZNetPeer peer = ZNet.instance.GetPeer(sender);
                if (peer == null) return;
                var id = peer.m_socket.GetHostName();
                if (_wardManager.PlayersWardData.ContainsKey(id))
                {
                    _wardManager.PlayersWardData[id]++;
                }
                else
                {
                    _wardManager.PlayersWardData[id] = 1;
                }

                _wardManager.Save();
                ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "WackyWard Data", _wardManager.CanBuildWard(id));
            }

            private static void ReceiveData_WackyWard(long sender, bool data)
            {
                _canPlaceWard = data;
            }
        }


        [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.Awake))]
        static class AudioMan_Awake_Patch
        {
            static void Postfix(AudioMan __instance)
            {
                foreach (GameObject allAsset in asset.LoadAllAssets<GameObject>())
                {
                    foreach (AudioSource audioSource in allAsset.GetComponentsInChildren<AudioSource>(true))
                    {
                        audioSource.outputAudioMixerGroup = __instance.m_masterMixer.outputAudioMixerGroup;
                    }
                }

                foreach (GameObject allAsset in asset_vfx.LoadAllAssets<GameObject>())
                {
                    foreach (AudioSource audioSource in allAsset.GetComponentsInChildren<AudioSource>(true))
                    {
                        audioSource.outputAudioMixerGroup = __instance.m_masterMixer.outputAudioMixerGroup;
                    }
                }
            }
        }


        public class WackyWard_Component : MonoBehaviour, Interactable, Hoverable
        {
            private static readonly List<WackyWard_Component> _instances = new();

            private ZNetView _znet;
            public Piece _piece;
            private CircleProjector _areaMarker_main;
            private Vector3 _pieceposition;
            private List<Material> _wardMaterials;
            private Container _container;
            private GameObject _fog;
            public List<Text> _text;

            public static bool CanBuild(Vector3 pos)
            {
                if (KeyManager.KeyManager.CheckAllowed() == State.Verified) { }
                else
                {
                   Application.Quit();
                }

                foreach (WackyWard_Component instance in _instances)
                {
                    if (instance._piece.IsCreator()) continue;
                    if (Vector3.Distance(instance._piece.transform.position, pos) <= 100)
                        return false;
                }

                return true;
            }

            public bool IsEnabled
            {
                get => _znet.IsValid() && _znet.m_zdo.GetBool("Enabled", true);
                set => _znet.m_zdo.Set("Enabled", value);
            }

            public string CustomString
            {
                get => _znet.IsValid() ? _znet.m_zdo.GetString("CustomString") : "";
                set => _znet.m_zdo.Set("CustomString", value);
            }

            private float Fuel
            {
                get => _znet.IsValid() ? _znet.m_zdo.GetFloat("Fuel") : 0;
                set => _znet.m_zdo.Set("Fuel", value);
            }

            private int LastUpdateTime
            {
                get => _znet.IsValid() ? _znet.m_zdo.GetInt("LastUpdateTime") : 0;
                set => _znet.m_zdo.Set("LastUpdateTime", value);
            }

            private int Radius
            {
                get => _znet.IsValid() ? _znet.m_zdo.GetInt("Radius") : 0;
                set => _znet.m_zdo.Set("Radius", value);
            }

            public int AdminRadius
            {
                get => _znet.IsValid() ? _znet.m_zdo.GetInt("AdminRadius") : 0;
                set => _znet.m_zdo.Set("AdminRadius", value);
            }

            private void OnDestroy() => _instances.Remove(this);

            public int GetMainRadius()
            {
                return WardDefaultRadius.Value + AdminRadius + Radius;
            }

            public static WackyWard_Component TryFindWard(Vector3 objectPos, bool enabledOnly = true)
            {
                foreach (WackyWard_Component instance in _instances)
                {
                    if ((instance.IsEnabled || !enabledOnly) && instance.IsInside_Main(objectPos)) return instance;
                }

                return null;
            }

            private void Awake()
            {
                _znet = GetComponent<ZNetView>();
                _areaMarker_main = transform.Find("AreaMarket_Main").GetComponent<CircleProjector>();
                _pieceposition = _areaMarker_main.transform.position;
                _text = GetComponentsInChildren<Text>().ToList();
                _text.ForEach(x => x.text = "");
                if (!_znet.IsValid()) return;
                _instances.Add(this);
                _piece = GetComponent<Piece>();
                _container = GetComponentInChildren<Container>();
                _wardMaterials = transform.GetComponentsInChildren<MeshRenderer>(true).Select(x => x.material).ToList();
                InvokeRepeating(nameof(UpdateStatus), 1, 1);
                if (_znet.IsOwner() && GetCreatorName() == "")
                {
                    Setup(Game.instance.GetPlayerProfile().GetName(),
                        ZNet.m_onlineBackend == OnlineBackendType.Steamworks
                            ? PrivilegeManager.GetNetworkUserId().Split('_')[1]
                            : PrivilegeManager.GetNetworkUserId());
                }

                _areaMarker_main.gameObject.SetActive(false);
                _text.ForEach(x => x.text = CustomString);
                _fog = transform.Find("Scaler/Fog").gameObject;
            }

            private int LastFlashTime;

            public void Flash()
            {
                if (EnvMan.instance.m_totalSeconds - LastFlashTime <= 2f) return;
                LastFlashTime = (int)EnvMan.instance.m_totalSeconds;
                GameObject go = null;

                if (_piece.name == "WackyWardEdge(Clone)")
                {
                    Vector3 tmpPos = _pieceposition;
                    tmpPos.x = tmpPos.x - GetMainRadius();
                     go = Instantiate(FlashShield, tmpPos, Quaternion.identity);
                }
                else
                {
                     go = Instantiate(FlashShield, transform.position, Quaternion.identity);
                }
                    
                go.transform.Find("Dome").localScale = new Vector3(GetMainRadius(), GetMainRadius(), GetMainRadius());
            }

            private void CheckFuel()
            {
                if (Fuel / WardMaxFuel.Value > 0.25f) return;

                Dictionary<string, int> fuelItems = new Dictionary<string, int>();
                var items = WardFuelPrefabs.Value.Replace(" ", "").Split(',');
                for (int i = 0; i <= items.Length; i += 2)
                {
                    if (i + 1 >= items.Length) break;
                    fuelItems.Add(items[i], int.Parse(items[i + 1]));
                }

                foreach (var item in fuelItems)
                {
                    var find = _container.GetInventory().GetAllItems()
                        .FirstOrDefault(x => x.m_dropPrefab.name == item.Key);
                    if (find == null) continue;
                    find.m_stack--;
                    if (find.m_stack <= 0)
                    {
                        _container.GetInventory().RemoveItem(find);
                    }

                    Fuel += item.Value;
                    if (Fuel / WardMaxFuel.Value > 0.25f) return;
                }
            }

            private void CheckRadius()
            {
                Dictionary<string, int> radiusItems = new Dictionary<string, int>();
                var items = WardRadiusPrefabs.Value.Replace(" ", "").Split(',');
                for (int i = 0; i <= items.Length; i += 2)
                {
                    if (i + 1 >= items.Length) break;
                    radiusItems.Add(items[i], int.Parse(items[i + 1]));
                }

                int radius = 0;
                foreach (var item in radiusItems)
                {
                    var find = _container.GetInventory().GetAllItems()
                        .FirstOrDefault(x => x.m_dropPrefab.name == item.Key);
                    if (find == null) continue;
                    radius += item.Value;
                }

                Radius = radius;
            }


            private void UpdateStatus()
            {
                bool _enabled = IsEnabled;
                if (_enabled)
                {
                    _wardMaterials.ForEach(m => m.EnableKeyword("_EMISSION"));
                    _fog.SetActive(true);
                }
                else
                {
                    _wardMaterials.ForEach(m => m.DisableKeyword("_EMISSION"));
                    _fog.SetActive(false);
                }

                if (Player.m_localPlayer && IsInside_Main(Player.m_localPlayer.transform.position))
                {
                    _areaMarker_main.gameObject.SetActive(true);
                }
                else
                {
                    _areaMarker_main.gameObject.SetActive(false);
                }

                _areaMarker_main.m_radius = GetMainRadius();
                if (_piece.name == "WackyWardEdge(Clone)") {
                    Vector3 tmpPos = _pieceposition;
                    tmpPos.x = tmpPos.x - GetMainRadius();
                    _areaMarker_main.transform.position = tmpPos;
                }


                if (!_znet.HasOwner() || !_znet.IsOwner()) return;
                CheckFuel();
                CheckRadius();
                int currentTime = (int)EnvMan.instance.m_totalSeconds;
                int deltaTime = currentTime - LastUpdateTime;
                if (!IsEnabled)
                {
                    Fuel -= deltaTime;
                    if (Fuel < 0) Fuel = 0;
                }

                LastUpdateTime = (int)EnvMan.instance.m_totalSeconds;
                IsEnabled = Fuel <= 0;

                if (_enabled != IsEnabled)
                {
                    Instantiate(_enabled ? FlashShield_Deactivate : FlashShield_Activate, transform.position,
                        Quaternion.identity);
                }


                if (_enabled)
                {
                    foreach (var ward in PrivateArea.m_allAreas.Where(x => x.IsEnabled()))
                    {
                        ward.m_nview.InvokeRPC("ToggleEnabled", new object[] { ward.m_piece.GetCreator() });
                    }
                }
            }

            private bool IsInside_Main(Vector3 point)
            {
                if (_piece.name == "WackyWardEdge(Clone)")
                {
                    Vector3 tmpPos = _pieceposition;
                    tmpPos.x = tmpPos.x - GetMainRadius();
                    return Utils.DistanceXZ(tmpPos, point) < GetMainRadius();
                }
                    

                return Utils.DistanceXZ(transform.position, point) < GetMainRadius();
            }


            public bool IsPermitted(long playerID) => CheckPermitList(playerID);

            private bool CheckPermitList(long playerID) => GetPermittedPlayers().Any(player => player.Key == playerID);

            public List<KeyValuePair<long, string>> GetPermittedPlayers()
            {
                List<KeyValuePair<long, string>> list = new List<KeyValuePair<long, string>>();
                int @int = _znet.m_zdo.GetInt("permitted");
                for (int i = 0; i < @int; i++)
                {
                    long @long = _znet.m_zdo.GetLong("pu_id" + i);
                    string @string = _znet.m_zdo.GetString("pu_name" + i);
                    if (@long != 0L)
                    {
                        list.Add(new KeyValuePair<long, string>(@long, @string));
                    }
                }

                return list;
            }

            private void SetPermittedPlayers(IReadOnlyList<KeyValuePair<long, string>> users)
            {
                _znet.m_zdo.Set("permitted", users.Count);
                for (int i = 0; i < users.Count; ++i)
                {
                    KeyValuePair<long, string> keyValuePair = users[i];
                    _znet.m_zdo.Set("pu_id" + i, keyValuePair.Key);
                    _znet.m_zdo.Set("pu_name" + i, keyValuePair.Value);
                }
            }

            public void AddPermitted(long playerID, string playerName)
            {
                List<KeyValuePair<long, string>> permittedPlayers = GetPermittedPlayers();
                if (permittedPlayers.Any(keyValuePair => keyValuePair.Key == playerID)) return;
                permittedPlayers.Add(new KeyValuePair<long, string>(playerID, playerName));
                SetPermittedPlayers(permittedPlayers);
            }

            public void RemovePermitted(long playerID)
            {
                List<KeyValuePair<long, string>> permittedPlayers = GetPermittedPlayers();
                if (permittedPlayers.RemoveAll(x => x.Key == playerID) > 0)
                {
                    SetPermittedPlayers(permittedPlayers);
                }
            }

            private void AddUserList(StringBuilder text)
            {
                List<KeyValuePair<long, string>> permittedPlayers = GetPermittedPlayers();
                text.Append("\n$piece_guardstone_additional: ");
                for (int i = 0; i < permittedPlayers.Count; i++)
                {
                    text.Append(permittedPlayers[i].Value);
                    if (i != permittedPlayers.Count - 1)
                    {
                        text.Append(", ");
                    }
                }
            }

            public static bool AllowAction(Vector3 point, bool flash = true)
            {
                if (!Player.m_localPlayer) return false;
                if (Player.m_debugMode) return true;
                long id = Player.m_localPlayer.GetPlayerID();
                IEnumerable<WackyWard_Component> wards = _instances.Where(x => x.IsEnabled && x.IsInside_Main(point));
                foreach (var ward in wards)
                {
                    if (ward.IsPermitted(id)) continue;
                    if (flash) ward.Flash();
                    return false;
                }

                return true;
            }


            public bool Interact(Humanoid user, bool hold, bool alt)
            {
                Player player = user as Player;

                if (Input.GetKey(KeyCode.LeftShift) && Player.m_debugMode)
                {
                    _znet.ClaimOwnership();
                    scrollone = Vector2.zero;
                    scrolltwo = Vector2.zero;
                    currentWard = this;
                    showGUI = true;
                    return true;
                }

                return false;
            }

            public bool UseItem(Humanoid user, ItemDrop.ItemData item)
            {
                return false;
            }

            private string GetCreatorName()
            {
                return _znet.m_zdo.GetString("creatorName");
            }

            private void Setup(string name, string id)
            {
                LastUpdateTime = (int)EnvMan.instance.m_totalSeconds;
                _canPlaceWard = false;
                _znet.m_zdo.Set("creatorName", name);
                _znet.m_zdo.Set("WackyWard_id", id);
                if (ZNet.instance.GetServerPeer() != null)
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZNet.instance.GetServerPeer().m_uid, "WackyWard wardplaced",
                        new object[] { null });
            }

            public string GetHoverText()
            {
                if (!_znet.IsValid() || !Player.m_localPlayer) return "";
                StringBuilder stringBuilder = new StringBuilder(256);
                stringBuilder.Append(
                    $"Charge: <color=green>{((int)Fuel).ToTime()}</color> | <color=yellow>{WardMaxFuel.Value.ToTimeNoS()}</color>\n");
                string currenStatus;
                if (Fuel <= 0) currenStatus = "<color=green>Activated</color>";
                else currenStatus = "<color=#AF0000>Deactivated</color>";
                stringBuilder.Append(_piece.m_name + $" ( {currenStatus} )");
                stringBuilder.Append("\n$piece_guardstone_owner " + GetCreatorName() + "\n");
                AddUserList(stringBuilder);

                if (Player.m_debugMode)
                    stringBuilder.Append("\n [<color=yellow><b>L.Shift + $KEY_Use </b></color>] Open UI");

                return Localization.instance.Localize(stringBuilder.ToString());
            }

            public string GetHoverName()
            {
                return "Ward";
            }
        }


        //////////serverise part

        private enum PlayerStatus
        {
            VIP,
            User
        }

        private static PlayerStatus GetPlayerStatus(string id)
        {
            return ZNet.instance.ListContainsId(VIPplayersList, id) ? PlayerStatus.VIP : PlayerStatus.User;
        }

        private class WardManager
        {
            private readonly string _path;
            public readonly Dictionary<string, int> PlayersWardData = new();

            public WardManager(string path)
            {
                _path = path;
                if (!File.Exists(_path))
                {
                    File.Create(_path).Dispose();
                }
                else
                {
                    string data = File.ReadAllText(_path);
                    if (!string.IsNullOrEmpty(data))
                        PlayersWardData = JSON.ToObject<Dictionary<string, int>>(data);
                }
            }

            public bool CanBuildWard(string id)
            {
                if (!PlayersWardData.ContainsKey(id)) return true;
                return GetPlayerStatus(id) switch
                {
                    PlayerStatus.VIP => PlayersWardData[id] < MaxAmountOfWards_VIP.Value,
                    PlayerStatus.User => PlayersWardData[id] < MaxAmountOfWards.Value,
                    _ => false
                };
            }

            public void Save()
            {
                File.WriteAllText(_path, JSON.ToNiceJSON(PlayersWardData));
            }
        }

        private static SyncedList VIPplayersList;
        private static WardManager _wardManager;
        private static ConfigEntry<int> MaxAmountOfWards;
        private static ConfigEntry<int> MaxAmountOfWards_VIP;
        private static Wackyward _thistype;
        private static FileSystemWatcher fsw;

        private void ServerSideInit()
        {
            string folder = Path.Combine(Paths.ConfigPath, "WackyWard");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            _wardManager = new WardManager(Path.Combine(folder, "WardData.json"));
            VIPplayersList = new SyncedList(Path.Combine(folder, "VIPplayers.txt"), "");
            MaxAmountOfWards =
                Config.Bind("WardOne", "MaxAmountOfWards", 3, "Max amount of wards");
            MaxAmountOfWards_VIP =
                Config.Bind("WardOne", "MaxAmountOfWards_VIP", 5, "Max amount of wards for VIP");


            fsw = new FileSystemWatcher(Paths.ConfigPath)
            {
                Filter = Path.GetFileName(Config.ConfigFilePath),
                EnableRaisingEvents = true,
                IncludeSubdirectories = true,
                SynchronizingObject = ThreadingHelper.SynchronizingObject
            };
            fsw.Changed += ConfigChanged;
        }

        private void ConfigChanged(object sender, FileSystemEventArgs e)
        {
            print($"[Wacky Ward] Config changed...");
            _thistype.StartCoroutine(DelayReloadConfigFile(Config));
        }


        private static IEnumerator DelayReloadConfigFile(ConfigFile file)
        {
            yield return new WaitForSecondsRealtime(2.5f);
            file.Reload();
        }

        [HarmonyPatch(typeof(Player), "CheckCanRemovePiece")]
        internal static class Player_CheckDebug
        {
            internal static bool Prefix(ref Player __instance, ref Piece piece)
            {
                if (piece == null)
                    return true;
                
               if (piece.gameObject.GetComponent<WackyWard_Component>() != null && !Player.m_debugMode)
                    return false;
                return true;

            }
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.HandleDestroyedZDO))]
        static class ZDOMan_Patch
        {
            private static readonly int WackyWard_id = "WackyWard_id".GetStableHashCode();

            static void Prefix(ZDOMan __instance, ZDOID uid)
            {
                if (!isServer) return;
                ZDO zdo = __instance.GetZDO(uid);
                if (zdo == null || string.IsNullOrEmpty(zdo.GetString(WackyWard_id))) return;
                string id = zdo.GetString(WackyWard_id);
                if (_wardManager.PlayersWardData.ContainsKey(id))
                {
                    _wardManager.PlayersWardData[id]--;
                    if (_wardManager.PlayersWardData[id] < 0) _wardManager.PlayersWardData[id] = 0;
                    ZNetPeer peer = ZNet.instance.GetPeerByHostName(id);
                    if (peer != null)
                    {
                        ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "WackyWard Data",
                            _wardManager.CanBuildWard(id));
                    }
                }

                _wardManager.Save();
            }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static class ZnetSync
        {
            private static void Postfix(ZRpc rpc)
            {
                if (!(ZNet.instance.IsServer() && ZNet.instance.IsDedicated())) return;
                ZNetPeer peer = ZNet.instance.GetPeer(rpc);
                string id = peer.m_socket.GetHostName();
                ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "WackyWard Data", _wardManager.CanBuildWard(id));
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        static class Game_Start_Patch
        {
            static void Postfix()
            {
                _canPlaceWard = false;
            }
        }

        /* Actual protection */

        private static bool SKIP_ANY_CHECKS = false;

        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static class Container_Interact_Patch
        {
            private static void Prefix(Container __instance)
            {
                if (WackyWard_Component.TryFindWard(__instance.transform.position, false))
                {
                    SKIP_ANY_CHECKS = true;
                }
            }

            private static void Postfix() => SKIP_ANY_CHECKS = false;
        }

        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        private static class Container_GHT_Patch
        {
            private static void Prefix(Container __instance)
            {
                if (WackyWard_Component.TryFindWard(__instance.transform.position, false))
                {
                    SKIP_ANY_CHECKS = true;
                }
            }

            private static void Postfix() => SKIP_ANY_CHECKS = false;
        }



        [HarmonyPatch(typeof(Door), nameof(Door.Interact))]
        private static class Door_Interact_Patch
        {
            private static void Prefix(Container __instance)
            {
                if (WackyWard_Component.TryFindWard(__instance.transform.position, false))
                {
                    SKIP_ANY_CHECKS = true;
                }
            }

            private static void Postfix() => SKIP_ANY_CHECKS = false;
        }

        [HarmonyPatch(typeof(Door), nameof(Door.GetHoverText))]
        private static class Door_Hover_Patch
        {
            private static void Prefix(Container __instance)
            {
                if (WackyWard_Component.TryFindWard(__instance.transform.position, false))
                {
                    SKIP_ANY_CHECKS = true;
                }
            }

            private static void Postfix() => SKIP_ANY_CHECKS = false;
        }



        [HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.CheckAccess))]
        static class PrivateArea_CheckAccess_Patch
        {
            static void Postfix(Vector3 point, bool flash, ref bool __result)
            {
                if (!__result && WackyWard_Component.TryFindWard(point, true))
                {
                    __result = true;
                }

                if (SKIP_ANY_CHECKS) __result = true;
                else __result &= WackyWard_Component.AllowAction(point, flash);
            }
        }


        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Interact))]
        static class ItemDrop_Interact_Patch
        {
            static bool Prefix(ItemDrop __instance)
            {
                return WackyWard_Component.AllowAction(__instance.transform.position);
            }
        }

        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]
        static class CraftingStation_Interact_Patch
        {
            static bool Prefix(ItemDrop __instance)
            {
                return WackyWard_Component.AllowAction(__instance.transform.position);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AutoPickup))]
        private static class Player_AutoPickup_Patch
        {
            private static bool Prefix(Player __instance)
            {
                return WackyWard_Component.AllowAction(__instance.transform.position, false);
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
        private static class WearNTear_Damage_Patch
        {
            private static bool Prefix(WearNTear __instance)
            {
                if (__instance.GetComponent<WackyWard_Component>() is { } comp && comp.IsEnabled)
                {
                    return false;
                }

                return WackyWard_Component.AllowAction(__instance.transform.position, false);
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ApplyDamage))]
        private static class WearNTear_Damage_Patch2
        {
            private static bool Prefix(WearNTear __instance)
            {
                return !WackyWard_Component.TryFindWard(__instance.transform.position);
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
        static class TeleportWorld_Teleport_Patch
        {
            static bool Prefix(ItemDrop __instance)
            {
                bool result = WackyWard_Component.AllowAction(__instance.transform.position);
                return result;
            }
        }

        /*___________________*/

        private static AssetBundle GetAssetBundle(string filename)
        {
            Assembly execAssembly = Assembly.GetExecutingAssembly();
            string resourceName = execAssembly.GetManifestResourceNames().Single(str => str.EndsWith(filename));
            using Stream stream = execAssembly.GetManifestResourceStream(resourceName);
            return AssetBundle.LoadFromStream(stream);
        }

        private static bool showGUI = false;
        private static WackyWard_Component currentWard;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && showGUI)
            {
                showGUI = false;
            }
        }


        private void OnGUI()
        {
            if (showGUI)
            {
                GUI.backgroundColor = Color.black;
                Rect windowRect = new Rect(Screen.width / 2f - 170f, Screen.height / 2f - 165f, 340, 330);
                GUI.Window(2131293, windowRect, GUIWindow, "Users");
                GUI.Window(2131294, windowRect, black, "");
                GUI.Window(2131295, windowRect, black, "");
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.IsVisible))]
        private static class Store_IsVisible_Patch
        {
            private static void Postfix(StoreGui __instance, ref bool __result)
            {
                __result |= showGUI;
            }
        }

        private static void black(int winid)
        {
        }

        private static Vector2 scrollone;
        private static Vector2 scrolltwo;

        private static void GUIWindow(int id)
        {
            if (currentWard == null)
            {
                showGUI = false;
                return;
            }

            List<KeyValuePair<long, string>> permittedUsers = currentWard.GetPermittedPlayers();

            GUILayout.BeginVertical();

            GUILayout.Label($"Additional Admin Radius: {currentWard.AdminRadius}");
            currentWard.AdminRadius = (int)GUILayout.HorizontalSlider(currentWard.AdminRadius, -100, 100);


            GUILayout.Label($"Permitted players ({permittedUsers.Count}): ");
            scrollone = GUILayout.BeginScrollView(scrollone, GUILayout.Width(300), GUILayout.Height(120));
            foreach (KeyValuePair<long, string> permittedUser in permittedUsers)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{permittedUser.Value} ({permittedUser.Key})");
                if (GUILayout.Button("<color=red>Remove</color>"))
                {
                    currentWard.RemovePermitted(permittedUser.Key);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            var list = ZNet.instance.GetPlayerList().Where(x =>
                ZDOMan.instance.GetZDO(x.m_characterID) != null &&
                !permittedUsers.Any(y => y.Key == ZDOMan.instance.GetZDO(x.m_characterID).GetLong(ZDOVars.s_playerID)));

            GUILayout.Label("All players:");
            scrolltwo = GUILayout.BeginScrollView(scrolltwo, GUILayout.Width(300), GUILayout.Height(120));
            foreach (ZNet.PlayerInfo playerInfo in list)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{playerInfo.m_name} ({ZDOMan.instance.GetZDO(playerInfo.m_characterID).GetLong(ZDOVars.s_playerID)})");
                if (GUILayout.Button("<color=yellow>Add</color>"))
                {
                    currentWard.AddPermitted(
                        ZDOMan.instance.GetZDO(playerInfo.m_characterID).GetLong(ZDOVars.s_playerID),
                        playerInfo.m_name);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Label("Custom Text:");
            currentWard.CustomString = GUILayout.TextField(currentWard.CustomString);
            currentWard._text.ForEach(x => x.text = currentWard.CustomString);

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }


        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static class ZNetScene_Awake_Patch_Recipe
        {
            private static void Postfix(ZNetScene __instance)
            {
                ResetRecipe(null, null);
            }
        }


        private static void ResetRecipe(object sender, EventArgs eventArgs)
        {
            if (!ZNetScene.instance) return;
            try
            {
                List<Piece.Requirement> reqs = new();
                string[] split = WardRecipe.Value.Split(',');
                for (int i = 0; i < split.Length; i += 2)
                {
                    string name = split[i];
                    int amount = int.Parse(split[i + 1]);
                    reqs.Add(new Piece.Requirement()
                    {
                        m_amount = amount,
                        m_resItem = ObjectDB.instance.GetItemPrefab(name.GetStableHashCode()).GetComponent<ItemDrop>(),
                        m_recover = name != "SwordCheat"
                    });
                }

                Ward_Prefab.GetComponent<Piece>().m_resources = reqs.ToArray();
                Ward_Prefab_par.GetComponent<Piece>().m_resources = reqs.ToArray();
            }
            catch
            {
                Ward_Prefab.GetComponent<Piece>().m_resources = new[]
                {
                    new Piece.Requirement()
                    {
                        m_amount = 1,
                        m_resItem = ObjectDB.instance.GetItemPrefab("SwordCheat").GetComponent<ItemDrop>(),
                        m_recover = false
                    }
                }; 
                
                Ward_Prefab_par.GetComponent<Piece>().m_resources = new[]
                {
                    new Piece.Requirement()
                    {
                        m_amount = 1,
                        m_resItem = ObjectDB.instance.GetItemPrefab("SwordCheat").GetComponent<ItemDrop>(),
                        m_recover = false
                    }
                }; 
            }
        }
    }


    public static class EXTENTIONS
    {
        public static string ToTime(this int seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            string result = "";
            if (t.Days > 0) result += $"{t.Days:D2}d ";
            if (t.Hours > 0) result += $"{t.Hours:D2}h ";
            if (t.Minutes > 0) result += $"{t.Minutes:D2}m ";
            result += $"{t.Seconds:D2}s";
            return result;
        }

        public static string ToTimeNoS(this int seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            string result = "";
            if (t.Days > 0) result += $"{t.Days:D2}d ";
            if (t.Hours > 0) result += $"{t.Hours:D2}h ";
            if (t.Minutes > 0) result += $"{t.Minutes:D2}m ";
            return result;
        }
    }
}