using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Downed", "bmgjet", "1.0.1")]
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
            [JsonProperty(PropertyName = "Allow Looting Downed NPCs: ")] public bool NPCLoot { get; set; }
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
                NPCLoot = true,
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

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (config.NPCLoot) //exit hook straight away since npc can be looted.
                return;

            BasePlayer loot = entity.ToPlayer();
            if (loot == null)
                return;

            if (loot.IsWounded() && loot.IsNpc)
                NextTick(player.EndLooting);
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
            if (!downedPlayers.ContainsKey(player.userID))
            {
                //Creates a timer to switches to crawl
                downedPlayers.Add(player.userID, timer.Once(config.UIDelay, () =>
                {
                    if (!player.IsDead())
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
                                timer.Repeat(2, config.NPCDownTimer, () =>
                                {
                                    currentgun.primaryMagazine.contents = 0; //Keep unloading so cant shoot.
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
            elements.Add(new CuiPanel { Image = { Color = "1 1 1 0.2" }, RectTransform = { AnchorMin = "0.805 0.944", AnchorMax = "1 1" }, CursorEnabled = false }, "Overlay", "Downed1");
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

        [ChatCommand("down")]
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
        [ChatCommand("getup")]
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

