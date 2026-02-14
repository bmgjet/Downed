/*▄▄▄    ███▄ ▄███▓  ▄████  ▄▄▄██▀▀▀▓█████▄▄▄█████▓
▓█████▄ ▓██▒▀█▀ ██▒ ██▒ ▀█▒   ▒██   ▓█   ▀▓  ██▒ ▓▒
▒██▒ ▄██▓██    ▓██░▒██░▄▄▄░   ░██   ▒███  ▒ ▓██░ ▒░
▒██░█▀  ▒██    ▒██ ░▓█  ██▓▓██▄██▓  ▒▓█  ▄░ ▓██▓ ░ 
░▓█  ▀█▓▒██▒   ░██▒░▒▓███▀▒ ▓███▒   ░▒████▒ ▒██▒ ░ 
░▒▓███▀▒░ ▒░   ░  ░ ░▒   ▒  ▒▓▒▒░   ░░ ▒░ ░ ▒ ░░   
▒░▒   ░ ░  ░      ░  ░   ░  ▒ ░▒░    ░ ░  ░   ░    
 ░    ░ ░      ░   ░ ░   ░  ░ ░ ░      ░    ░      
 ░             ░         ░  ░   ░      ░  ░*/
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Downed", "bmgjet", "1.0.4")]
    [Description("Allow NPC Knock Downs")]

    class Downed : RustPlugin
    {
        private PluginConfig config;
        public Dictionary<ulong, Timer> downedPlayers = new Dictionary<ulong, Timer> { { (ulong)0, null } };

        #region Configuration
        private class PluginConfig
        {
            [JsonProperty(PropertyName = "Bleed out timer: ")] public int Bleedout { get; set; }
            [JsonProperty(PropertyName = "How long before NPC gets back up: ")] public int NPCDownTimer { get; set; }
            [JsonProperty(PropertyName = "Percentage for NPC to bleedout: ")] public int NPCBleedOutChance { get; set; }
            [JsonProperty(PropertyName = "Block NPC Looting: ")] public bool NPCNoLoot { get; set; }
            [JsonProperty(PropertyName = "Ignore Scarecrow: ")] public bool IgnoreScarecrow { get; set; }
            [JsonProperty(PropertyName = "Ignore Murderer: ")] public bool IgnoreMurderer { get; set; }
            [JsonProperty(PropertyName = "SFX On Downed (Delete link to disable): ")] public string[] SFX { get; set; }
            [JsonProperty(PropertyName = "SFX PlayTime: ")] public float SFXPlayTime { get; set; }
        }

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                Bleedout = 120,
                NPCDownTimer = 20,
                NPCBleedOutChance = 50,
                NPCNoLoot = true,
                IgnoreScarecrow = false,
                IgnoreMurderer = false,
                SFX = new string[] { "https://github.com/bmgjet/Stations/raw/main/Help.Me.mp3", "https://github.com/bmgjet/Downed/raw/main/Help.Me.mp3" },
                SFXPlayTime = 10,
            };
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            Config.WriteObject(GetDefaultConfig(), true);
            config = Config.ReadObject<PluginConfig>();
        }
        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }
        #endregion

        #region Hooks
        void Init()
        {
            config = Config.ReadObject<PluginConfig>();
            if (config == null)
            {
                LoadDefaultConfig();
            }
        }

        void OnPlayerRecover(BasePlayer player) { removedowned(player); }

        private object CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            if (target == null || looter == null || !config.NPCNoLoot) return null;
            if (target.IsWounded() && target.IsNpc)
            {
                NextTick(looter.EndLooting);
                return false;
            }
            return null;
        }

        private object OnNpcTarget(NPCPlayer npc, BaseEntity entity)
        {
            if (entity == null || npc == null) return null;
            if (npc.IsWounded() || npc.IsIncapacitated())
            {
                return true;
            }
            return null;
        }

        object OnPlayerWound(BasePlayer player, HitInfo info)
        {
            if (!downedPlayers.ContainsKey(player.userID))
            {
                try
                {
                    CreateSound(player, config.SFX[Convert.ToInt32(Math.Round(Convert.ToDouble(UnityEngine.Random.Range(Convert.ToSingle(0), Convert.ToSingle(config.SFX.Length - 1)))))]);
                }
                catch { }
                //Creates a timer to switches to crawl
                downedPlayers.Add(player.userID, timer.Once(5, () =>
                {
                    if (player == null) { return; }
                    if (!player.IsDead())
                    {
                        player.StopWounded(); //Reset the wounded state
                        downedPlayers[player.userID] = null; //Clear this timer
                        LongDown(player, info); //Custom wounded state with cui
                        player.SendNetworkUpdateImmediate();
                    }
                }));
            }
            return null; //Normal operation.
        }

        object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player != null)
            {
                //Check if ScareCrow
                if (config.IgnoreScarecrow)
                {
                    var combatEntity = player as BaseCombatEntity;
                    if (combatEntity != null && combatEntity.ShortPrefabName != "scarecrow")
                    {
                        return null; //Disables Downed
                    }
                }
                //Check if Murderer
                if (config.IgnoreMurderer)
                {
                    var combatEntity = player as BaseCombatEntity;
                    if (combatEntity != null && combatEntity.ShortPrefabName != "murderer")
                    {
                        return null; //Disables Downed
                    }
                }
                //Always enter wounded if not already.
                if (!downedPlayers.ContainsKey(player.userID))
                {
                    if (player.IsNpc) //Is NPC
                    {
                        if(player.HasParent() && player.GetParentEntity() is Tugboat) //Stop tugboat flip
                        {
                            return null;
                        }
                        HeldEntity heldEntity = player.GetHeldEntity(); //Get NPC Gun
                        if (heldEntity != null)
                        {
                            BaseProjectile currentgun = heldEntity as BaseProjectile;
                            if (currentgun != null)
                            {
                                currentgun.primaryMagazine.contents = 0;
                                var reloadloop = timer.Repeat(1.8f, config.NPCDownTimer, () =>
                                {
                                    try
                                    {
                                        if (currentgun != null)
                                        {
                                            currentgun.primaryMagazine.contents = 0; //Keep unloading so cant shoot.
                                        }
                                    }
                                    catch { }
                                });
                                timer.Once(config.NPCDownTimer + 5f, () =>
                                {
                                    try
                                    {
                                        if (reloadloop != null)
                                        {
                                            reloadloop.Destroy();
                                        }
                                    }
                                    catch { }
                                });
                            }
                        }
                        timer.Once(config.NPCDownTimer, () =>
                        {
                            if (player == null) { return; }
                            if (player.IsAlive())
                            {
                                //Chance of bleedout
                                if (UnityEngine.Random.Range(0f, 100f) < config.NPCBleedOutChance)
                                {
                                    player.StopWounded(); //NPC Gets up
                                    return;
                                }
                                player.DieInstantly();
                            }
                        });
                        player.BecomeWounded(info);
                        return false;
                    }
                }
            }
            return null; //Allow die
        }
        #endregion

        #region Code
        private void LongDown(BasePlayer player, HitInfo info, bool downed = true)
        {
            if (player == null) { return; }
            if (downed) //Down player
            {
                player.BecomeWounded(info);
                player.ProlongWounding(config.Bleedout); //time too bleed out
                player.SendNetworkUpdate();
                return;
            }
            player.StopWounded(); //End downed player.
            player.SendNetworkUpdate();
        }

        public void removedowned(BasePlayer player)
        {
            if (downedPlayers.ContainsKey(player.userID))
            {
                if (downedPlayers[player.userID] != null)
                {
                    //Clear timer
                    downedPlayers[player.userID].Destroy();
                    downedPlayers[player.userID] = null;
                }
                downedPlayers.Remove(player.userID);
            }
        }

        void DestroyMeshCollider(BaseEntity ent)
        {
            foreach (var mesh in ent.GetComponentsInChildren<MeshCollider>())
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private void CreateSound(BasePlayer player, string url)
        {
            if (downedPlayers.Count > 2 || !player.IsAlive()) return; //Stops massive spam since only 3 can play at a time within 400f radius
            //Spawns a boom box if SFX string is set. Places it under players location and plays the set SFX mp3.
            //Despawns when player dies or after SFXPlayTime setting.
            if (url == "") return;
            DeployableBoomBox boombox = GameManager.server.CreateEntity("assets/prefabs/voiceaudio/boombox/boombox.deployed.prefab", default(Vector3), default(Quaternion), true) as DeployableBoomBox;
            DestroyMeshCollider(boombox);
            boombox.SetParent(player);
            boombox.networkEntityScale = true;
            boombox.Spawn();
            boombox.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
            boombox.pickup.enabled = false;
            boombox.BoxController.ServerTogglePlay(false);
            boombox.BoxController.AssignedRadioBy = player.OwnerID;
            timer.Once(1f, () =>
            {
                if (boombox == null) { return; }
                boombox.BoxController.CurrentRadioIp = url;
                boombox.ClientRPC<string>(RpcTarget.NetworkGroup("OnRadioIPChanged"), boombox.BoxController.CurrentRadioIp);
                boombox.BoxController.ServerTogglePlay(true);
            });
        }

        public object cleanup(BasePlayer player)
        {
            removedowned(player); //Remove from custom downed list
            return null;
        }
        #endregion
    }
}
