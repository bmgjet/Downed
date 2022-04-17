using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Downed", "bmgjet", "1.0.3")]
    [Description("Extends knocked down timer and give cui to player. Allow NPC Knock Downs")]

    class Downed : RustPlugin
    {
        private static PluginConfig config;
        public Dictionary<ulong, Timer> downedPlayers = new Dictionary<ulong, Timer> { { (ulong)0, null } };

        #region Configuration
        private class PluginConfig
        {
            [JsonProperty(PropertyName = "Delay before showing getup CUI: ")] public int UIDelay { get; set; }
            [JsonProperty(PropertyName = "Countdown for getting back up: ")] public int Countdown { get; set; }
            [JsonProperty(PropertyName = "Bleedout timer: ")] public int Bleedout { get; set; }
            [JsonProperty(PropertyName = "Allow NPCs to be knocked down: ")] public bool NPCDowned { get; set; }
            [JsonProperty(PropertyName = "How long before NPC gets back up: ")] public int NPCDownTimer { get; set; }
            [JsonProperty(PropertyName = "Percentage for NPC to bleedout: ")] public int NPCBleedOutChance { get; set; }
            [JsonProperty(PropertyName = "Only NPCs get knocked: ")] public bool NPCOnly { get; set; }
            [JsonProperty(PropertyName = "Block NPC Looting: ")] public bool NPCNoLoot { get; set; }
            [JsonProperty(PropertyName = "Ignore Scarecrow: ")] public bool IgnoreScarecrow { get; set; }
            [JsonProperty(PropertyName = "Ignore Murderer: ")] public bool IgnoreMurderer { get; set; }
            [JsonProperty(PropertyName = "NPC Dont Shoot Downed: ")] public bool NPCIgnoreDowned { get; set; }
            [JsonProperty(PropertyName = "SFX On Downed (Delete link to disable): ")] public string[] SFX { get; set; }
            [JsonProperty(PropertyName = "SFX PlayTime: ")] public float SFXPlayTime { get; set; }
            [JsonProperty(PropertyName = "SFX Allow on NPC: ")] public bool SFXNPC { get; set; }
            [JsonProperty(PropertyName = "SFX Allow on PLAYER: ")] public bool SFXPLAYER { get; set; }
        }

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                UIDelay = 10,
                Countdown = 20,
                Bleedout = 120,
                NPCDowned = true,
                NPCDownTimer = 20,
                NPCBleedOutChance = 50,
                NPCOnly = false,
                NPCNoLoot = false,
                IgnoreScarecrow = false,
                IgnoreMurderer = false,
                NPCIgnoreDowned = false,
                SFX = new string[] { "https://github.com/bmgjet/Stations/raw/main/Help.Me.mp3", "https://github.com/bmgjet/Downed/raw/main/Help.Me.mp3" },
                SFXPlayTime = 10,
                SFXNPC = false,
                SFXPLAYER = false
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
        void Unload()
        {
            if (config != null)
            {
                config = null;
            }
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                cleanup(player);
            }
        }
        void OnPlayerDisconnected(BasePlayer player)
        {
            cleanup(player);
        }
        object OnPlayerRespawn(BasePlayer player)
        {
            return cleanup(player);
        }
        object OnPlayerRecover(BasePlayer player)
        {
            return cleanup(player);
        }
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
            BasePlayer target = entity.ToPlayer();
            if (target != null)
            {
                if (config.NPCIgnoreDowned && entity.ToPlayer().IsWounded())
                {
                    return true;
                }
            }
            return null;
        }
        private object OnEntityTakeDamage(BasePlayer player, HitInfo hitInfo)
        {
            Rust.DamageType damageType = hitInfo.damageTypes.GetMajorityDamageType();
            if (damageType == Rust.DamageType.Suicide || damageType == Rust.DamageType.Drowned)
            {
                //Allows player to Suicide without knock down.
                //Stops Players wounding under water.
                if (damageType == Rust.DamageType.Drowned)
                {
                    if (player.health > 0)
                        return null;
                }
                player.DieInstantly();
                return false;
            }
            return null;
        }
        object OnPlayerWound(BasePlayer player)
        {
            if (!downedPlayers.ContainsKey(player.userID) && !config.NPCOnly)
            {
                try
                {
                    CreateSound(player, config.SFX[Convert.ToInt32(Math.Round(Convert.ToDouble(UnityEngine.Random.Range(Convert.ToSingle(0), Convert.ToSingle(config.SFX.Length - 1)))))]);
                }
                catch { }
                //Creates a timer to switches to crawl
                downedPlayers.Add(player.userID, timer.Once(config.UIDelay, () =>
                {
                    if (player != null && !player.IsDead())
                    {
                        player.StopWounded(); //Reset the wounded state
                        downedPlayers[player.userID] = null; //Clear this timer
                        LongDown(player); //Custom wounded state with cui
                        player.SendNetworkUpdateImmediate();
                    }
                }));
            }
            return null; //Normal operation.
        }
        object OnPlayerDeath(BasePlayer player)
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
                        if (!config.NPCDowned)
                        {
                            return null; //Disable NPC downing and die
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
                                    currentgun.primaryMagazine.contents = 0; //Keep unloading so cant shoot.
                                });
                                timer.Once(config.NPCDownTimer + 5f, () =>
                                {
                                    try
                                    {
                                        reloadloop.Destroy();
                                    }
                                    catch { }
                                });
                            }
                        }
                        timer.Once(config.NPCDownTimer, () =>
                        {
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
                    }
                    if (config.NPCOnly && !player.IsNpc)
                    {
                        return null;//NPCs only.
                    }
                    player.BecomeWounded();
                    return false; //Prevent die
                }
                cuidestroy(player);
            }
            return null; //Allow die
        }
        #endregion

        #region Code
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
            if (player.IsNpc && !config.SFXNPC) return;
            if (!player.IsNpc && !config.SFXPLAYER) return;

            if (url == "") return;
            SphereEntity sph = (SphereEntity)GameManager.server.CreateEntity("assets/prefabs/visualization/sphere.prefab", default(Vector3), default(Quaternion), true);
            DestroyMeshCollider(sph);
            sph.Spawn();
            DeployableBoomBox boombox = GameManager.server.CreateEntity("assets/prefabs/voiceaudio/boombox/boombox.deployed.prefab", default(Vector3), default(Quaternion), true) as DeployableBoomBox;
            DestroyMeshCollider(boombox);
            boombox.SetParent(sph);
            boombox.Spawn();
            boombox.pickup.enabled = false;
            boombox.BoxController.ServerTogglePlay(false);
            boombox.BoxController.AssignedRadioBy = player.OwnerID;
            sph.LerpRadiusTo(0.01f, 1f);
            timer.Once(1f, () => {
                if (sph != null)
                    sph.SetParent(player);
                sph.transform.localPosition = new Vector3(0, -1.5f, 0f);
                boombox.BoxController.CurrentRadioIp = url;
                boombox.BoxController.baseEntity.ClientRPC<string>(null, "OnRadioIPChanged", boombox.BoxController.CurrentRadioIp);
                boombox.BoxController.ServerTogglePlay(true);
                timer.Once(config.SFXPlayTime, () =>
                {
                    try
                    {
                        if (sph != null)
                            sph?.Kill();
                    }
                    catch { }
                });
            });
            sph.SendNetworkUpdateImmediate();
        }
        public object cleanup(BasePlayer player)
        {
            removedowned(player); //Remove from custom downed list
            cuidestroy(player);   //Removes all CUI
            return null;
        }
        void UserUI(BasePlayer player, string msg)
        {
            if (msg == "") return;
            cuidestroy(player);
            var elements = new CuiElementContainer();
            elements.Add(new CuiPanel { Image = { Color = "1 1 1 0.2" }, RectTransform = { AnchorMin = "0.805 0.944", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", "Downed1");
            elements.Add(new CuiLabel { Text = { Text = msg, FontSize = 15, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.806 0.975", AnchorMax = "0.99 0.999" } }, "Overlay", "Downed2");
            elements.Add(new CuiButton { Button = { Command = "global.playergetup", Color = "0 0.78 0 0.5" }, RectTransform = { AnchorMin = "0.815 0.950", AnchorMax = "0.900 0.970" }, Text = { Text = "Get Up", FontSize = 10, Align = TextAnchor.MiddleCenter } }, "Overlay", "Downed3");
            elements.Add(new CuiButton { Button = { Command = "global.playerdie", Color = "0.78 0 0 0.5" }, RectTransform = { AnchorMin = "0.910 0.950", AnchorMax = "0.995 0.970" }, Text = { Text = "Respawn", FontSize = 10, Align = TextAnchor.MiddleCenter } }, "Overlay", "Downed4");
            CuiHelper.AddUi(player, elements);
        }
        void GetUpTimer(BasePlayer player, string msg)
        {
            if (msg == "") return;
            cuidestroy(player);
            var elements = new CuiElementContainer();
            elements.Add(new CuiLabel { Text = { Text = msg, FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.806 0.955", AnchorMax = "0.99 0.989" } }, "Overlay", "Downed5");
            CuiHelper.AddUi(player, elements);
        }
        public void cuidestroy(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "Downed1");
            CuiHelper.DestroyUi(player, "Downed2");
            CuiHelper.DestroyUi(player, "Downed3");
            CuiHelper.DestroyUi(player, "Downed4");
            CuiHelper.DestroyUi(player, "Downed5");
        }
        private void LongDown(BasePlayer player, bool downed = true)
        {
            if (downed) //Down player
            {
                player.BecomeWounded();
                player.ProlongWounding(config.Bleedout); //time too bleed out
                player.SendNetworkUpdate();
                UserUI(player, "<color=red>You Have Been Downed!</color>");
                return;
            }
            player.StopWounded(); //End downed player.
            player.SendNetworkUpdate();
            cuidestroy(player);
        }
        #endregion

        #region Commands
        [ConsoleCommand("playergetup")]
        private void playergetup(ConsoleSystem.Arg arg)
        {
            //Getup
            var player = arg.Connection.player as BasePlayer;
            if (player == null) return;

            cuidestroy(player);
            player.SetPlayerFlag(global::BasePlayer.PlayerFlags.Incapacitated, true); //Flip onto back.
            player.SetServerFall(true);
            int i = 0;
            //Start Countdown timer
            var countdowntimer = timer.Repeat(1, config.Countdown + 1, () =>
            {
                try
                {
                    if (!player.IsDead() && downedPlayers.ContainsKey(player.userID))
                    {
                        GetUpTimer(player, "<color=red>Get UP IN</color> " + (config.Countdown - i).ToString());
                        if (i++ >= config.Countdown)
                        {
                            cuidestroy(player);
                            LongDown(player, false);
                            removedowned(player);
                            return;
                        }
                    }
                    else
                    {
                        removedowned(player);
                        return;
                    }
                }
                catch { }
            });
            timer.Once(config.Countdown + 5f, () =>
             {
                 try
                 {
                     countdowntimer.Destroy();
                 }
                 catch { }
             });
            if (downedPlayers.ContainsKey(player.userID))
            {
                downedPlayers.Remove(player.userID);
            }
            downedPlayers.Add(player.userID, countdowntimer);
        }

        [ConsoleCommand("playerdie")]
        private void playerdie(ConsoleSystem.Arg arg)
        {
            //Respawn
            var player = arg.Connection.player as BasePlayer;
            if (player == null) return;
            player.DieInstantly();
            cuidestroy(player);
            removedowned(player);
        }

        [ChatCommand("playerdown")]
        private void CmdplayerFall(BasePlayer player, string command, string[] args)
        {
            //Trigger player by name to become wounded
            if (player.IsAdmin)
            {
                BasePlayer moddedpayer;
                if (args.Length != 0)
                {
                    moddedpayer = BasePlayer.FindAwakeOrSleeping(args[0]);
                    if (moddedpayer == null)
                    {
                        player.ChatMessage("Couldnt find base player " + args[0]);
                        return;
                    }
                }
                else
                {
                    player.ChatMessage("No ARGS provided.");
                    return;
                }
                moddedpayer.BecomeWounded();
            }
        }
        [ChatCommand("playerup")]
        private void CmdplayerGetup(BasePlayer player, string command, string[] args)
        {
            //Trigger player by name to get back up.
            if (player.IsAdmin)
            {
                BasePlayer moddedpayer;
                if (args.Length != 0)
                {
                    moddedpayer = BasePlayer.FindAwakeOrSleeping(args[0]);
                    if (moddedpayer == null)
                    {
                        player.ChatMessage("Couldnt find base player " + args[0]);
                        return;
                    }
                }
                else
                {
                    player.ChatMessage("No ARGS provided.");
                    return;
                }
                removedowned(player);
                LongDown(moddedpayer, false);
            }
        }
        #endregion
    }
}