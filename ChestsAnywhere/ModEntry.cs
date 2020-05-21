using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using Netcode;
using Pathoschild.Stardew.ChestsAnywhere.Framework;
using Pathoschild.Stardew.ChestsAnywhere.Framework.Containers;
using Pathoschild.Stardew.ChestsAnywhere.Menus.Overlays;
using Pathoschild.Stardew.Common;
using Pathoschild.Stardew.Common.Messages;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.Network;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace Pathoschild.Stardew.ChestsAnywhere
{
    /// <summary>The mod entry point.</summary>
    internal class ModEntry : Mod
    {
        /*********
        ** Fields
        *********/
        /// <summary>The mod configuration.</summary>
        private ModConfig Config;

        /// <summary>The configured key bindings.</summary>
        private ModConfigKeys Keys;

        /// <summary>The internal mod settings.</summary>
        private ModData Data;

        /// <summary>Encapsulates logic for finding chests.</summary>
        private ChestFactory ChestFactory;


        /****
        ** State
        ****/
        /// <summary>The selected in-game inventory.</summary>
        private IList<Item> SelectedInventory;

        /// <summary>The overlay for the current menu which which lets the player navigate and edit chests (or <c>null</c> if not applicable).</summary>
        private IStorageOverlay CurrentOverlay;

        public static IEnumerable<ChestPsuedoGameLocation> CachedLocations;
        private uint LastUpdateTick = 0;
        private uint ChestHash = 0;
        private long playerHash = 0;
        private int trackedLocs = 0;
        private bool haveAddedChests = false;


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides methods for interacting with the mod directory, such as read/writing a config file or custom JSON files.</param>
        public override void Entry(IModHelper helper)
        {
            // initialize
            this.Config = helper.ReadConfig<ModConfig>();
            this.Keys = this.Config.Controls.ParseControls(helper.Input, this.Monitor);
            this.Data = helper.Data.ReadJsonFile<ModData>("data.json") ?? new ModData();
            this.ChestFactory = new ChestFactory(helper.Data, helper.Multiplayer, helper.Reflection, helper.Translation, this.Config.EnableShippingBin);

            // hook events
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.UpdateTicking += this.OnUpdateTicking;
            helper.Events.Display.RenderedHud += this.OnRenderedHud;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.GameLoop.OneSecondUpdateTicked += this.OnOneSecondUpdateTicked;
            helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;

            // validate translations
            if (!helper.Translation.GetTranslations().Any())
                this.Monitor.Log("The translation files in this mod's i18n folder seem to be missing. The mod will still work, but you'll see 'missing translation' messages. Try reinstalling the mod to fix this.", LogLevel.Warn);
        }

        private void OnModMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            this.Monitor.Log($"Message Received, {e.Type}", LogLevel.Trace);
            if (e.Type == "AllChestData")
            {
                List<byte[]> data = e.ReadAs<List<byte[]>>();
                XmlSerializer bf = new XmlSerializer(typeof(ChestPsuedoGameLocation));
                List<ChestPsuedoGameLocation> gameLocs = new List<ChestPsuedoGameLocation>();
                foreach (byte[] datum in data)
                {
                    MemoryStream ms = new MemoryStream(datum);
                    var pair = (ChestPsuedoGameLocation)bf.Deserialize(ms);
                    gameLocs.Add(pair);
                    foreach(var location in CommonHelper.GetLocations())
                    {
                        if(location.Name == "Farm")
                        {
                            var fields = location.NetFields.NetFields.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                            object tt = fields[0].GetValue(location.NetFields.NetFields);
                            int trackerIndex = ((List<INetSerializable>)tt).FindIndex(e => { if (e is NetString net) { return net.Value == "ChestTracker"; } else { return false; } });
                            if (trackerIndex != -1)
                            {
                                ((NetCollection<Chest>)((List<INetSerializable>)tt)[trackerIndex + 1]).Add(pair.Chest);
                                
                            }
                        }
                    }
                }
                CachedLocations = gameLocs;
            }
        }

        private void OnOneSecondUpdateTicked(object sender, OneSecondUpdateTickedEventArgs e)
        {
            //Way forward
            
            this.LastUpdateTick++;
            long checkPlayerHash = 0;
            bool areClientsConnected = false;
            foreach(var player in this.Helper.Multiplayer.GetConnectedPlayers())
            {
                areClientsConnected = true;
                checkPlayerHash += player.PlayerID;
            }
            if(this.playerHash == 0 && areClientsConnected)
            {
                this.playerHash = checkPlayerHash;
                this.ChestHash = 0;
            }
            else
            {
                if(this.playerHash != checkPlayerHash)
                {
                    this.ChestHash = 0;
                    this.playerHash = checkPlayerHash;
                }
            }
            uint currentChestHash = 0;
            List<string> chestNames = new List<string>();
            List<ChestPsuedoGameLocation> chests = new List<ChestPsuedoGameLocation>();
            NetCollection<Chest> c = new NetCollection<Chest>();
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
                return;
            foreach (var location in CommonHelper.GetLocations())
            {
                PsuedoGameLocation pLoc = new PsuedoGameLocation(location);
                if (location.Name != "BugLand" && location.Name != "DeepWoods")
                {
                    foreach(KeyValuePair <Vector2, SObject> pair in location.Objects.Pairs)
                    {
                        Vector2 tile = pair.Key;
                        SObject obj = pair.Value;

                        // chests
                        if (obj is Chest chest && chest.playerChest.Value)
                        {
                            c.Add(chest);
                            chests.Add(new ChestPsuedoGameLocation(chest, pLoc));
                            currentChestHash += (uint)chest.Name.GetHashCode();
                        }
                    }
                }
            }

            if(!this.haveAddedChests)
            {
                this.haveAddedChests = true;
                foreach (var location in CommonHelper.GetLocations())
                {
                    if (location.Name == "Farm")
                    {
                        location.NetFields.AddField(new NetString("ChestTracker"));
                        location.NetFields.AddField(c);
                    }
                }
            }
            XmlSerializer bf = new XmlSerializer(typeof(ChestPsuedoGameLocation));
            MemoryStream ms = new MemoryStream();
            List<byte[]> locations = new List<byte[]>();
            if (this.ChestHash != currentChestHash)
            {
                foreach (var location in CommonHelper.GetLocations())
                {
                    if (location.Name == "Farm")
                    {
                        var fields = location.NetFields.NetFields.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                        object tt = fields[0].GetValue(location.NetFields.NetFields);
                        int trackerIndex = ((List<INetSerializable>)tt).FindIndex(e => { if (e is NetString net) { return net.Value == "ChestTracker"; } else { return false; } });
                        if(trackerIndex != -1)
                        {
                            ((NetCollection<Chest>)((List<INetSerializable>)tt)[trackerIndex + 1]).Clear();
                            foreach (var netChest in c)
                            {
                                ((NetCollection<Chest>)((List<INetSerializable>)tt)[trackerIndex + 1]).Add(netChest);
                            }
                        }
                        
                    }
                }
                this.ChestHash = currentChestHash;
                foreach (var chest in chests)
                {
                    bf.Serialize(ms, chest);
                    locations.Add(ms.ToArray());
                    ms = new MemoryStream();
                }
                this.Monitor.Log($"Sending Message", LogLevel.Trace);
                this.Helper.Multiplayer.SendMessage<List<byte[]>>(
                    locations,
                    "AllChestData",
                    modIDs: new[] { this.ModManifest.UniqueID },
                    playerIDs: this.Helper.Multiplayer.GetConnectedPlayers().Select(e => { return e.PlayerID; }).ToArray());
            }
        }


        /*********
        ** Private methods
        *********/
        /// <summary>The method invoked after the player loads a saved game.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            // validate game version
            string versionError = this.ValidateGameVersion();
            if (versionError != null)
            {
                this.Monitor.Log(versionError, LogLevel.Error);
                CommonHelper.ShowErrorMessage(versionError);
            }

            // show multiplayer limitations warning
            if (!Context.IsMainPlayer)
            {
                this.Monitor.Log("Multiplayer limitations: you can only access chests in your current location (since you're not the main player). This is due to limitations in the game's sync logic.", LogLevel.Info);
            }
        }

        /// <summary>The method invoked when the interface has finished rendering.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            // show chest label
            if (this.Config.ShowHoverTooltips)
            {
                ManagedChest cursorChest = this.ChestFactory.GetChestFromTile(Game1.currentCursorTile);
                if (cursorChest != null && !cursorChest.HasDefaultName())
                {
                    Vector2 tooltipPosition = new Vector2(Game1.getMouseX(), Game1.getMouseY()) + new Vector2(Game1.tileSize / 2f);
                    CommonHelper.DrawHoverBox(e.SpriteBatch, cursorChest.DisplayName, tooltipPosition, Game1.viewport.Width - tooltipPosition.X - Game1.tileSize / 2f);
                }
            }
        }

        /// <summary>Raised before the game state is updated (≈60 times per second).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnUpdateTicking(object sender, UpdateTickingEventArgs e)
        {
            this.ChangeOverlayIfNeeded();
        }

        /// <summary>Raised after the game state is updated (≈60 times per second).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            foreach (var location in CommonHelper.GetLocations())
            {
                if (location.Name == "Farm")
                {
                    if(!this.haveAddedChests)
                    {
                        this.haveAddedChests = true;
                        var fields2 = location.NetFields.NetFields.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                        object tt2 = fields2[0].GetValue(location.NetFields.NetFields);
                        var newNetFields = new List<INetSerializable>();
                        foreach(var item in ((List<INetSerializable>)tt2))
                        {
                            newNetFields.Add(item);
                        }
                        newNetFields.Add(new NetString("ChestTracker"));
                        newNetFields.Add(new NetCollection<Chest>());
                        fields2[0].SetValue(location.NetFields.NetFields, newNetFields);
                    }
                    var fields = location.NetFields.NetFields.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                    object tt = fields[0].GetValue(location.NetFields.NetFields);
                    if(((List<INetSerializable>)tt).Count != this.trackedLocs)
                    {
                        this.trackedLocs = ((List<INetSerializable>)tt).Count;
                        this.Monitor.Log($"{this.trackedLocs}", LogLevel.Info);
                    }
                    
                }
            }
                this.ChangeOverlayIfNeeded();
        }

        /// <summary>The method invoked when the player presses a button.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            try
            {
                ModConfigKeys keys = this.Keys;

                // open menu
                if (keys.Toggle.JustPressedUnique())
                {
                    // open if no conflict
                    if (Game1.activeClickableMenu == null)
                    {
                        if (Context.IsPlayerFree && !Game1.player.UsingTool)
                            this.OpenMenu();
                    }

                    // open from inventory if it's safe to close the inventory screen
                    else if (Game1.activeClickableMenu is GameMenu gameMenu && gameMenu.currentTab == GameMenu.inventoryTab)
                    {
                        IClickableMenu inventoryPage = gameMenu.pages[GameMenu.inventoryTab];
                        if (inventoryPage.readyToClose())
                            this.OpenMenu();
                    }
                }
            }
            catch (Exception ex)
            {
                this.HandleError(ex, "handling key input");
            }
        }

        /// <summary>Change the chest UI overlay if needed to match the current menu.</summary>
        /// <remarks>Since the menu gets reopened whenever the chest inventory changes, this method needs to be called before/after tick to avoid a visible UI flicker.</remarks>
        private void ChangeOverlayIfNeeded()
        {
            IClickableMenu menu = Game1.activeClickableMenu;

            // already matches menu
            if (this.CurrentOverlay?.ForMenuInstance == menu)
                return;

            // remove old overlay
            if (this.CurrentOverlay != null)
            {
                this.CurrentOverlay?.Dispose();
                this.CurrentOverlay = null;
            }

            // get open chest
            ManagedChest chest = this.ChestFactory.GetChestFromMenu(menu);
            if (chest == null)
                return;

            // reopen shipping box in standard chest UI if needed
            // This is called in two cases:
            // - When the player opens the shipping bin directly, it opens the shipping bin view instead of the full chest view.
            // - When the player changes the items in the chest view, it reopens itself but loses the constructor args (e.g. highlight function).
            if (this.Config.EnableShippingBin && chest.Container is ShippingBinContainer)
            {
                if (menu is ItemGrabMenu chestMenu && (!chestMenu.showReceivingMenu || !(chestMenu.inventory.highlightMethod?.Target is ShippingBinContainer)))
                {
                    menu = (ItemGrabMenu)chest.OpenMenu();
                    Game1.activeClickableMenu = menu;
                }
            }

            // add overlay
            RangeHandler range = this.GetCurrentRange();
            ManagedChest[] chests = this.ChestFactory.GetChests(range, cachedLocations: CachedLocations, excludeHidden: true, alwaysIncludeContainer: chest.Container).ToArray();
            bool isAutomateInstalled = this.Helper.ModRegistry.IsLoaded("Pathoschild.Automate");
            switch (menu)
            {
                case ItemGrabMenu chestMenu:
                    this.CurrentOverlay = new ChestOverlay(chestMenu, chest, chests, this.Config, this.Keys, this.Helper.Events, this.Helper.Input, this.Helper.Reflection, this.Helper.Translation, showAutomateOptions: isAutomateInstalled && chest.CanConfigureAutomate);
                    break;

                case ShopMenu shopMenu:
                    this.CurrentOverlay = new ShopMenuOverlay(shopMenu, chest, chests, this.Config, this.Keys, this.Helper.Events, this.Helper.Input, this.Helper.Reflection, this.Helper.Translation, showAutomateOptions: isAutomateInstalled && chest.CanConfigureAutomate);
                    break;
            }
            this.CurrentOverlay.OnChestSelected += selected =>
            {
                this.SelectedInventory = selected.Container.Inventory;
                Game1.activeClickableMenu = selected.OpenMenu();
            };
            this.CurrentOverlay.OnAutomateOptionsChanged += this.NotifyAutomateOfChestUpdate;
        }

        /// <summary>Open the menu UI.</summary>
        private void OpenMenu()
        {
            if (this.Config.Range == ChestRange.None)
                return;

            // handle disabled location
            if (this.IsDisabledLocation(Game1.currentLocation))
            {
                CommonHelper.ShowInfoMessage(this.Helper.Translation.Get("errors.disabled-from-here"), duration: 1000);
                return;
            }

            // get chests
            RangeHandler range = this.GetCurrentRange();
            ManagedChest[] chests = this.ChestFactory.GetChests(range, cachedLocations: CachedLocations, excludeHidden: true).ToArray();
            ManagedChest selectedChest = chests.FirstOrDefault(p => p.Container.IsSameAs(this.SelectedInventory)) ?? chests.FirstOrDefault();

            // show error
            if (selectedChest == null)
            {
                string translationKey = this.GetNoChestsFoundErrorKey();
                CommonHelper.ShowInfoMessage(this.Helper.Translation.Get(translationKey), duration: 1000);
                return;
            }

            // render menu
            Game1.activeClickableMenu = selectedChest.OpenMenu();
        }

        /// <summary>Notify Automate that a chest's automation options updated.</summary>
        /// <param name="chest">The chest that was updated.</param>
        private void NotifyAutomateOfChestUpdate(ManagedChest chest)
        {
            long hostId = Game1.MasterPlayer.UniqueMultiplayerID;
            var message = new AutomateUpdateChestMessage { LocationName = chest.PsuedoLocation != null ? chest.PsuedoLocation.Name : chest.Location.Name, Tile = chest.Tile };
            this.Helper.Multiplayer.SendMessage(message, nameof(AutomateUpdateChestMessage), modIDs: new[] { "Pathoschild.Automate" }, playerIDs: new[] { hostId });
        }

        /// <summary>Validate that the game versions match the minimum requirements, and return an appropriate error message if not.</summary>
        private string ValidateGameVersion()
        {
            if (Constant.MinimumApiVersion.IsNewerThan(Constants.ApiVersion))
                return $"The Chests Anywhere mod requires a newer version of SMAPI. Please update SMAPI from {Constants.ApiVersion} to {Constant.MinimumApiVersion}.";

            return null;
        }

        /// <summary>Log an error and warn the user.</summary>
        /// <param name="ex">The exception to handle.</param>
        /// <param name="verb">The verb describing where the error occurred (e.g. "looking that up").</param>
        private void HandleError(Exception ex, string verb)
        {
            this.Monitor.Log($"Something went wrong {verb}:\n{ex}", LogLevel.Error);
            CommonHelper.ShowErrorMessage($"Huh. Something went wrong {verb}. The error log has the technical details.");
        }

        /// <summary>Get whether remote access is disabled from the given location.</summary>
        /// <param name="location">The game location.</param>
        private bool IsDisabledLocation(GameLocation location)
        {
            if (this.Config.DisabledInLocations == null)
                return false;

            return
                this.Config.DisabledInLocations.Contains(location.Name)
                || (location is MineShaft && location.Name.StartsWith("UndergroundMine") && this.Config.DisabledInLocations.Contains("UndergroundMine"));
        }

        /// <summary>Get the range for the current context.</summary>
        private RangeHandler GetCurrentRange()
        {
            ChestRange range = this.IsDisabledLocation(Game1.currentLocation)
                ? ChestRange.None
                : this.Config.Range;
            return new RangeHandler(this.Data.WorldAreas, range, Game1.currentLocation);
        }

        private RangeHandler GetAllRange()
        {
            return new RangeHandler(this.Data.WorldAreas, ChestRange.Unlimited, Game1.currentLocation);
        }

        /// <summary>Get the error translation key to show if no chests were found.</summary>
        private string GetNoChestsFoundErrorKey()
        {
            if (this.Config.Range == ChestRange.CurrentLocation || !Context.IsMainPlayer)
                return "errors.no-chests-in-location";

            if (this.Config.Range != ChestRange.Unlimited)
                return "errors.no-chests-in-range";

            return "errors.no-chests";
        }
    }
}
